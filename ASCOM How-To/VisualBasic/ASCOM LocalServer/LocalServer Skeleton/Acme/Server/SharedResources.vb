'
' ================
' Shared Resources
' ================
'
' This class is a container for all shared resources that may be needed
' by the drivers served by the Local Server. 
'
' NOTES:
'
'	* ALL DECLARATIONS MUST BE Shared HERE!! INSTANCES OF THIS CLASS MUST NEVER BE CREATED!
'
' Written by:	Bob Denny	29-May-2007
' Modified by Chris Rowland and Peter Simpson to handle multiple hardware devices March 2011
' Modified by Dan Karmann to port C# version to Visual Basic .NET   January 2015
'
Imports System
Imports System.Collections.Generic
Imports System.Text
Imports ASCOM

''' <summary>
''' The resources shared by all drivers and devices, in this example it's a serial port with a shared SendMessage method
''' an idea for locking the message and handling connecting is given.
''' In reality extensive changes will probably be needed.
''' Multiple drivers means that several applications connect to the same hardware device, aka a hub.
''' Multiple devices means that there are more than one instance of the hardware, such as two focusers.
''' In this case there needs to be multiple instances of the hardware connector, each with it's own connection count.
''' </summary>
Public NotInheritable Class SharedResources
    Private Sub New()
        ' so can't be instantiated
    End Sub

    ' object used for locking to prevent multiple drivers accessing common code at the same time
    Private Shared ReadOnly lockObject As New Object()

    ' Shared serial port. This will allow multiple drivers to use one single serial port.
    Private Shared s_sharedSerial As New ASCOM.Utilities.Serial()   ' Shared serial port
    Private Shared s_z As Integer = 0       ' counter for the number of connections to the serial port
    '
    ' Public access to shared resources
    '

#Region "single serial port connector"
    '
    ' This region shows a way that a single serial port could be connected to by multiple 
    ' drivers.
    '
    ' Connected() is used to handle the connections to the port.
    '
    ' SendMessage() is a way that messages could be sent to the hardware without
    ' conflicts between different drivers.
    '
    ' All this is for a single connection, multiple connections would need multiple ports
    ' and a way to handle connecting and disconnection from them - see the
    ' multi driver handling section for ideas.
    '

    ''' <summary>
    ''' Shared serial port
    ''' </summary>
    Public Shared ReadOnly Property SharedSerial() As ASCOM.Utilities.Serial
        Get
            Return s_sharedSerial
        End Get
    End Property

    ''' <summary>
    ''' Number of connections to the shared serial port.
    ''' </summary>
    Public Shared Property Connections() As Integer
        Get
            Return s_z
        End Get
        Set(ByVal value as Integer)
            s_z = value
        End Set
    End Property

    ''' <summary>
    ''' Example of a shared SendMessage() method, the SyncLock
    ''' prevents different drivers tripping over one another.
    ''' It needs error handling and assumes that the message will be sent unchanged
    ''' and that the reply will always be terminated by a "#" character.
    ''' </summary>
    ''' <param name="message">message string to be sent</param>
    ''' <returns></returns>
    Public Shared Function SendMessage(message As String) As String
        SyncLock lockObject
            SharedSerial.Transmit(message)
            ' TODO replace this with your requirements
            Return SharedSerial.ReceiveTerminated("#")
        End SyncLock
    End Function

    ''' <summary>
    ''' Example of handling connecting to and disconnection from the
    ''' shared serial port.
    ''' Needs error handling.
    ''' The port name etc. needs to be set up first. This could be done by the driver
    ''' checking Connected and if it's False, setting up the port before setting Connected to true.
    ''' It could also be put here.
    ''' </summary>
    Public Shared Property Connected() As Boolean
        Get
            Return SharedSerial.Connected
        End Get
        Set(ByVal value as Boolean)
            SyncLock lockObject
                If value Then
                    If s_z = 0 Then
                        SharedSerial.Connected = True
                    End If
                    s_z += 1
                Else
                    s_z -= 1
                    If s_z <= 0 Then
                        SharedSerial.Connected = False
                    End If
                End If
            End SyncLock
        End Set
    End Property

#End Region

#Region "Multi Driver handling"
    ' This section illustrates how multiple drivers could be handled.
    ' It's for drivers where multiple connections to the hardware can be made and ensures that the
    ' hardware is only disconnected from when all the connected devices have disconnected.

    ' It is NOT a complete solution!  This is to give ideas of what can - or should be done.
    '
    ' An alternative would be to move the hardware control here, handle connecting and disconnecting,
    ' and provide the device with a suitable connection to the hardware.
    '
    ''' <summary>
    ''' Dictionary carrying device connections.
    ''' The Key is the connection number that identifies the device, it could be the COM port name,
    ''' USB ID or IP Address, the Value is the DeviceHardware class.
    ''' </summary>
    Private Shared connectedDevices As New Dictionary(Of String, DeviceHardware)()

    ''' <summary>
    ''' This is called in the driver Connected(True) property.
    ''' It adds the device Id to the list of devices if it's not there and increments the device count.
    ''' </summary>
    ''' <param name="deviceId"></param>
    Public Shared Sub Connect(deviceId As String)
        SyncLock lockObject
            If Not connectedDevices.ContainsKey(deviceId) Then
                connectedDevices.Add(deviceId, New DeviceHardware())
            End If
            connectedDevices(deviceId).count += 1           ' increment the value
        End SyncLock
    End Sub

    Public Shared Sub Disconnect(deviceId As String)
        SyncLock lockObject
            If connectedDevices.ContainsKey(deviceId) Then
                connectedDevices(deviceId).count -= 1
                If connectedDevices(deviceId).count <= 0 Then
                    connectedDevices.Remove(deviceId)
                End If
            End If
        End SyncLock
    End Sub

    Public Shared Function IsConnected(deviceId As String) As Boolean
        If connectedDevices.ContainsKey(deviceId) Then
            Return (connectedDevices(deviceId).count > 0)
        Else
            Return False
        End If
    End Function

#End Region

End Class

''' <summary>
''' Skeleton of a hardware class. All this does is hold a count of the connections.
''' In reality, extra code will be needed to handle the hardware in some way.
''' </summary>
Public Class DeviceHardware
    Private m_count As Integer
    Friend Property count() As Integer
        Get
            Return m_count
        End Get
        Set(ByVal value as Integer)
            m_count = Value
        End Set
    End Property

    Friend Sub New()
        count = 0
    End Sub
End Class

'#Region "ServedClassName attribute"
'''' <summary>
'''' This is only needed if the driver is targeted at platform 5.5, it is included with Platform 6
'''' </summary>
'<System.AttributeUsage(AttributeTargets.Class, Inherited := False, AllowMultiple := False)> _
'Public NotInheritable Class ServedClassNameAttribute
'    Inherits Attribute
'    ' See the attribute guidelines at
'    ' http://go.microsoft.com/fwlink/?LinkId=85236
'
'    ''' <summary>
'    ''' Gets or sets the 'friendly name' of the served class, as registered with the ASCOM Chooser.
'    ''' </summary>
'    ''' <value>The 'friendly name' of the served class.</value>
'    Private m_DisplayName As String
'    Public Property DisplayName() As String
'        Get
'            Return m_DisplayName
'        End Get
'        Private Set(ByVal value as String)
'            m_DisplayName = Value
'        End Set
'    End Property
'
'    ''' <summary>
'    ''' Initializes a new instance of the <see cref="ServedClassNameAttribute"/> class.
'    ''' </summary>
'    ''' <param name="servedClassName">The 'friendly name' of the served class.</param>
'    Public Sub New(servedClassName As String)
'        DisplayName = servedClassName
'    End Sub
'End Class
'#End Region
