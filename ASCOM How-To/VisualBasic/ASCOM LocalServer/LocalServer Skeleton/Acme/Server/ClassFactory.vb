Imports System
Imports System.Runtime.InteropServices
Imports System.Collections
Imports Microsoft.VisualBasic

#Region "VB Definition of IClassFactory"
'
' Provide a definition of theCOM IClassFactory interface.
'
' ComImport - This interface originated from COM.
' ComVisible - Must not be exposed to COM!!!
' InterfaceType - Indicate that this interface is not IDispatch-based.
' This GUID is the actual GUID of IClassFactory.
<ComImport, ComVisible(False), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), _
 Guid("00000001-0000-0000-C000-000000000046")> _
Public Interface IClassFactory
    Sub CreateInstance(pUnkOuter As IntPtr, ByRef riid As Guid, ByRef ppvObject As IntPtr)
    Sub LockServer(fLock As Boolean)
End Interface
#End Region

'
' Universal ClassFactory. Given a type as a parameter of the 
' constructor, it implements IClassFactory for any interface
' that the class implements. Magic!!!
'
Public Class ClassFactory
    Implements IClassFactory

#Region "Access to ole32.dll functions for class factories"

    ' Define two common GUID objects for public usage.
    Public Shared IID_IUnknown As New Guid("{00000000-0000-0000-C000-000000000046}")
    Public Shared IID_IDispatch As New Guid("{00020400-0000-0000-C000-000000000046}")

    <Flags> _
    Private Enum CLSCTX As UInteger
        CLSCTX_INPROC_SERVER = &H1
        CLSCTX_INPROC_HANDLER = &H2
        CLSCTX_LOCAL_SERVER = &H4
        CLSCTX_INPROC_SERVER16 = &H8
        CLSCTX_REMOTE_SERVER = &H10
        CLSCTX_INPROC_HANDLER16 = &H20
        CLSCTX_RESERVED1 = &H40
        CLSCTX_RESERVED2 = &H80
        CLSCTX_RESERVED3 = &H100
        CLSCTX_RESERVED4 = &H200
        CLSCTX_NO_CODE_DOWNLOAD = &H400
        CLSCTX_RESERVED5 = &H800
        CLSCTX_NO_CUSTOM_MARSHAL = &H1000
        CLSCTX_ENABLE_CODE_DOWNLOAD = &H2000
        CLSCTX_NO_FAILURE_LOG = &H4000
        CLSCTX_DISABLE_AAA = &H8000
        CLSCTX_ENABLE_AAA = &H10000
        CLSCTX_FROM_DEFAULT_CONTEXT = &H20000
        CLSCTX_INPROC = CLSCTX_INPROC_SERVER Or CLSCTX_INPROC_HANDLER
        CLSCTX_SERVER = CLSCTX_INPROC_SERVER Or CLSCTX_LOCAL_SERVER Or CLSCTX_REMOTE_SERVER
        CLSCTX_ALL = CLSCTX_SERVER Or CLSCTX_INPROC_HANDLER
    End Enum

    <Flags> _
    Private Enum REGCLS As UInteger
        REGCLS_SINGLEUSE = 0
        REGCLS_MULTIPLEUSE = 1
        REGCLS_MULTI_SEPARATE = 2
        REGCLS_SUSPENDED = 4
        REGCLS_SURROGATE = 8
    End Enum
    '
    ' CoRegisterClassObject() is used to register a Class Factory
    ' into COM's internal table of Class Factories.
    '
    <DllImport("ole32.dll")> _
    Private Shared Function CoRegisterClassObject(<[In]()> ByRef rclsid As Guid, _
        <MarshalAs(UnmanagedType.IUnknown)> pUnk As Object, _
        dwClsContext As UInteger, _
        flags As UInteger, _
        ByRef lpdwRegister As UInteger) As Integer
    End Function
    '
    ' Called by a COM EXE Server that can register multiple class objects 
    ' to inform COM about all registered classes, and permits activation 
    ' requests for those class objects. 
    ' This function causes OLE to inform the SCM about all the registered 
    ' classes, and begins letting activation requests into the server process.
    '
    <DllImport("ole32.dll")> _
    Private Shared Function CoResumeClassObjects() As Integer
    End Function
    '
    ' Prevents any new activation requests from the SCM on all class objects
    ' registered within the process. Even though a process may call this API, 
    ' the process still must call CoRevokeClassObject for each CLSID it has 
    ' registered, in the apartment it registered in.
    '
    <DllImport("ole32.dll")> _
    Private Shared Function CoSuspendClassObjects() As Integer
    End Function
    '
    ' CoRevokeClassObject() is used to unregister a Class Factory
    ' from COM's internal table of Class Factories.
    '
    <DllImport("ole32.dll")> _
    Private Shared Function CoRevokeClassObject(dwRegister As UInteger) As Integer
    End Function
#End Region

#Region "Constructor and Private ClassFactory Data"

    Protected m_ClassType As Type
    Protected m_ClassId As Guid
    Protected m_InterfaceTypes As ArrayList
    Protected m_ClassContext As UInteger
    Protected m_Flags As UInteger
    Protected m_locked As UInt32 = 0
    Protected m_Cookie As UInteger
    Protected m_progid As String

    Public Sub New(thetype As Type)
        If thetype Is Nothing Then
            Throw New ArgumentNullException("thetype")
        End If
        m_ClassType = thetype

        'PWGS Get the ProgID from the MetaData
        m_progid = Marshal.GenerateProgIdForType(thetype)
        m_ClassId = Marshal.GenerateGuidForType(thetype)        ' Should be nailed down by [Guid(...)]
        m_ClassContext = CUInt(CLSCTX.CLSCTX_LOCAL_SERVER)      ' Default
        m_Flags = CUInt(REGCLS.REGCLS_MULTIPLEUSE) Or CUInt(REGCLS.REGCLS_SUSPENDED)    ' Default
        m_InterfaceTypes = New ArrayList()
        For Each T As Type In thetype.GetInterfaces()           ' Save all of the implemented interfaces
            m_InterfaceTypes.Add(T)
        Next
    End Sub

#End Region

#Region "Common ClassFactory Methods"
    Public Property ClassContext() As UInteger
        Get
            Return m_ClassContext
        End Get
        Set(ByVal value as UInteger)
            m_ClassContext = value
        End Set
    End Property

    Public Property ClassId() As Guid
        Get
            Return m_ClassId
        End Get
        Set(ByVal value as Guid)
            m_ClassId = value
        End Set
    End Property

    Public Property Flags() As UInteger
        Get
            Return m_Flags
        End Get
        Set(ByVal value as UInteger)
            m_Flags = value
        End Set
    End Property

    Public Function RegisterClassObject() As Boolean
        ' Register the class factory
        Dim i As Integer = CoRegisterClassObject(m_ClassId, Me, m_ClassContext, m_Flags, m_Cookie)
        Return (i = 0)
    End Function

    Public Function RevokeClassObject() As Boolean
        Dim i As Integer = CoRevokeClassObject(m_Cookie)
        Return (i = 0)
    End Function

    Public Shared Function ResumeClassObjects() As Boolean
        Dim i As Integer = CoResumeClassObjects()
        Return (i = 0)
    End Function

    Public Shared Function SuspendClassObjects() As Boolean
        Dim i As Integer = CoSuspendClassObjects()
        Return (i = 0)
    End Function
#End Region

#Region "IClassFactory Implementations"
    '
    ' Implement creation of the type and interface.
    '
    Private Sub IClassFactory_CreateInstance(pUnkOuter As IntPtr, ByRef riid As Guid, ByRef ppvObject As IntPtr) Implements IClassFactory.CreateInstance
        Dim nullPtr As New IntPtr(0)
        ppvObject = nullPtr

        '
        ' Handle specific requests for implemented interfaces
        '
        For Each iType As Type In m_InterfaceTypes
            If riid = Marshal.GenerateGuidForType(iType) Then
                ppvObject = Marshal.GetComInterfaceForObject(Activator.CreateInstance(m_ClassType), iType)
                Return
            End If
        Next
        '
        ' Handle requests for IDispatch or IUnknown on the class
        '
        If riid = IID_IDispatch Then
            ppvObject = Marshal.GetIDispatchForObject(Activator.CreateInstance(m_ClassType))
            Return
        ElseIf riid = IID_IUnknown Then
            ppvObject = Marshal.GetIUnknownForObject(Activator.CreateInstance(m_ClassType))
        Else
            '
            ' Oops, some interface that the class doesn't implement
            '
            Throw New COMException("No interface", CInt(Val(&H80004002UI)))
        End If
    End Sub

    Private Sub IClassFactory_LockServer(bLock As Boolean) Implements IClassFactory.LockServer
        If bLock Then
            Server.CountLock()
        Else
            Server.UncountLock()
        End If
        ' Always attempt to see if we need to shutdown this server application.
        Server.ExitIf()
    End Sub
#End Region
End Class
