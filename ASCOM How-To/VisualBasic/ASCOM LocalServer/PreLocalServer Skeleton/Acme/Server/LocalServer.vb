'
' ASCOM.Acme Local COM Server
'
' This is the core of a managed COM Local Server, capable of serving
' multiple instances of multiple interfaces, within a single
' executable. This implementes the equivalent functionality of VB6
' which has been extensively used in ASCOM for drivers that provide
' multiple interfaces to multiple clients (e.g. Meade Telescope
' and Focuser) as well as hubs (e.g., POTH).
'
' Written by: Robert B. Denny (Version 1.0.1, 29-May-2007)
' Modified by Chris Rowland and Peter Simpson to allow use with multiple devices of the same type March 2011
' Modified by Dan Karmann to port C# version to Visual Basic .NET   January 2015
'
'
Imports System
Imports System.IO
Imports System.Windows.Forms
Imports System.Drawing
Imports System.Collections
Imports System.Runtime.InteropServices
Imports System.Reflection
Imports ASCOM.Utilities
Imports Microsoft.Win32
Imports Microsoft.VisualBasic
Imports System.Text
Imports System.Threading
Imports System.Security.Principal
Imports System.Diagnostics
Imports ASCOM

Public NotInheritable Class Server
    Private Sub New()
        ' so can't be instantiated
    End Sub

#Region "Access to kernel32.dll, user32.dll, and ole32.dll functions"
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
    Private Enum COINIT As UInteger
        ' Initializes the thread for multi-threaded object concurrency.
        COINIT_MULTITHREADED = &H0
        ' Initializes the thread for apartment-threaded object concurrency. 
        COINIT_APARTMENTTHREADED = &H2
        ' Disables DDE for Ole1 support.
        COINIT_DISABLE_OLE1DDE = &H4
        ' Trades memory for speed.
        COINIT_SPEED_OVER_MEMORY = &H8
    End Enum

    <Flags> _
    Private Enum REGCLS As UInteger
        REGCLS_SINGLEUSE = 0
        REGCLS_MULTIPLEUSE = 1
        REGCLS_MULTI_SEPARATE = 2
        REGCLS_SUSPENDED = 4
        REGCLS_SURROGATE = 8
    End Enum


    ' CoInitializeEx() can be used to set the apartment model
    ' of individual threads.
    <DllImport("ole32.dll")> _
    Private Shared Function CoInitializeEx(pvReserved As IntPtr, dwCoInit As UInteger) As Integer
    End Function

    ' CoUninitialize() is used to uninitialize a COM thread.
    <DllImport("ole32.dll")> _
    Private Shared Sub CoUninitialize()
    End Sub

    ' PostThreadMessage() allows us to post a Windows Message to
    ' a specific thread (identified by its thread id).
    ' We will need this API to post a WM_QUIT message to the main 
    ' thread in order to terminate this application.
    <DllImport("user32.dll")> _
    Private Shared Function PostThreadMessage(idThread As UInteger, Msg As UInteger, wParam As UIntPtr, lParam As IntPtr) As Boolean
    End Function

    ' GetCurrentThreadId() allows us to obtain the thread id of the
    ' calling thread. This allows us to post the WM_QUIT message to
    ' the main thread.
    <DllImport("kernel32.dll")> _
    Private Shared Function GetCurrentThreadId() As UInteger
    End Function
#End Region

#Region "Private Data"
    Private Shared objsInUse As Integer             ' Keeps a count on the total number of objects alive.
    Private Shared serverLocks As Integer           ' Keeps a lock count on this application.
    Private Shared s_MainForm As frmMain = Nothing  ' Reference to our main form
    Private Shared s_ComObjectAssys As ArrayList    ' Dynamically loaded assemblies containing served COM objects
    Private Shared s_ComObjectTypes As ArrayList    ' Served COM object types
    Private Shared s_ClassFactories As ArrayList    ' Served COM object class factories
    Private Shared s_appId As String = "{a340cc92-1c7e-4073-88ae-6c3dc00ec707}"  ' Our AppId
    Private Shared ReadOnly lockObject As New Object()
#End Region

    ' This property returns the main thread's id.
    Private Shared m_MainThreadId As UInteger       ' Stores the main thread's thread id.
    Public Shared Property MainThreadId() As UInteger
        Get
            Return m_MainThreadId
        End Get
        Private Set(ByVal value as UInteger)
            m_MainThreadId = Value
        End Set
    End Property
    
    ' Used to tell if started by COM or manually
    Private Shared m_StartedByCOM As Boolean        ' True if server started by COM (-embedding)
    Public Shared Property StartedByCOM() As Boolean
        Get
            Return m_StartedByCOM
        End Get
        Private Set(ByVal value as Boolean)
            m_StartedByCOM = Value
        End Set
    End Property

#Region "Server Lock, Object Counting, and AutoQuit on COM startup"
    ' Returns the total number of objects alive currently.
    Public Shared ReadOnly Property ObjectsCount() As Integer
        Get
            SyncLock lockObject
                Return objsInUse
            End SyncLock
        End Get
    End Property

    ' This method performs a thread-safe incrementation of the objects count.
    Public Shared Function CountObject() As Integer
        ' Increment the global count of objects.
        Return Interlocked.Increment(objsInUse)
    End Function

    ' This method performs a thread-safe decrementation of the objects count.
    Public Shared Function UncountObject() As Integer
        ' Decrement the global count of objects.
        Return Interlocked.Decrement(objsInUse)
    End Function

    ' Returns the current server lock count.
    Public Shared ReadOnly Property ServerLockCount() As Integer
        Get
            SyncLock lockObject
                Return serverLocks
            End SyncLock
        End Get
    End Property

    ' This method performs a thread-safe incrementation of the 
    ' server lock count.
    Public Shared Function CountLock() As Integer
        ' Increment the global lock count of this server.
        Return Interlocked.Increment(serverLocks)
    End Function

    ' This method performs a thread-safe decrementation of the 
    ' server lock count.
    Public Shared Function UncountLock() As Integer
        ' Decrement the global lock count of this server.
        Return Interlocked.Decrement(serverLocks)
    End Function

    ' ExitIf() will check to see if the objects count and the server 
    ' lock count have both dropped to zero.
    '
    ' If so, and if we were started by COM, we post a WM_QUIT message to the main thread's
    ' message loop. This will cause the message loop to exit and hence the termination 
    ' of this application. If hand-started, then just trace that it WOULD exit now.
    '
    Public Shared Sub ExitIf()
        SyncLock lockObject
            If (ObjectsCount <= 0) AndAlso (ServerLockCount <= 0) Then
                If StartedByCOM Then
                    Dim wParam As New UIntPtr(0)
                    Dim lParam As New IntPtr(0)
                    PostThreadMessage(MainThreadId, &H12, wParam, lParam)
                End If
            End If
        End SyncLock
    End Sub
#End Region

    ' -----------------
    ' PRIVATE FUNCTIONS
    ' -----------------

#Region "Dynamic Driver Assembly Loader"
    '
    ' Load the assemblies that contain the classes that we will serve
    ' via COM. These will be located in the same folder as
    ' our executable.
    '
    Private Shared Function LoadComObjectAssemblies() As Boolean
        s_ComObjectAssys = New ArrayList()
        s_ComObjectTypes = New ArrayList()

        ' put everything into one folder, the same as the server.
        Dim assyPath As String = Assembly.GetEntryAssembly().Location
        assyPath = Path.GetDirectoryName(assyPath)

        Dim d As New DirectoryInfo(assyPath)
        For Each fi As FileInfo In d.GetFiles("*.dll")
            Dim aPath As String = fi.FullName
            '
            ' First try to load the assembly and get the types for
            ' the class and the class factory. If this doesn't work ????
            '
            Try
                Dim so As Assembly = Assembly.LoadFrom(aPath)
                'PWGS Get the types in the assembly
                Dim types As Type() = so.GetTypes()
                For Each ttype As Type In types
                    ' PWGS Now checks the type rather than the assembly
                    ' Check to see if the type has the ServedClassName attribute, only use it if it does.
                    Dim info As MemberInfo = ttype

                    Dim attrbutes As Object() = info.GetCustomAttributes(GetType(ServedClassNameAttribute), False)
                    If attrbutes.Length > 0 Then
                        'MessageBox.Show("Adding Type: " & ttype.Name & " " & ttype.FullName);
                        s_ComObjectTypes.Add(ttype)     'PWGS - much simpler
                        s_ComObjectAssys.Add(so)
                    End If
                Next
            Catch generatedExceptionName As BadImageFormatException
                ' Probably an attempt to load a Win32 DLL (i.e. not a .NET assembly)
                ' Just swallow the exception and continue to the next item.
                Continue For
            Catch ex As Exception
                MessageBox.Show("Failed to load served COM class assembly " & _
                    fi.Name & " - " & ex.Message, "Acme", _
                    MessageBoxButtons.OK, MessageBoxIcon.Stop)
                Return False
            End Try
        Next
        Return True
    End Function
#End Region

#Region "COM Registration and Unregistration"
    '
    ' Test if running elevated
    '
    Private Shared ReadOnly Property IsAdministrator() As Boolean
        Get
            Dim i As WindowsIdentity = WindowsIdentity.GetCurrent()
            Dim p As New WindowsPrincipal(i)
            Return p.IsInRole(WindowsBuiltInRole.Administrator)
        End Get
    End Property

    '
    ' Elevate by re-running ourselves with elevation dialog
    '
    Private Shared Sub ElevateSelf(arg As String)
        Dim si As New ProcessStartInfo()
        si.Arguments = arg
        si.WorkingDirectory = Environment.CurrentDirectory
        si.FileName = Application.ExecutablePath
        si.Verb = "runas"
        Try
            Process.Start(si)
        Catch ex As System.ComponentModel.Win32Exception
            MessageBox.Show("The Acme was not " & _
            CStr(IIf(arg = "/register", "registered", "unregistered")) & _
            " because you did not allow it.", "Acme", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        Catch ex As Exception
            MessageBox.Show(ex.ToString(), "Acme", MessageBoxButtons.OK, MessageBoxIcon.Stop)
        End Try
        Return
    End Sub

    '
    ' Do everything to register this for COM. Never use REGASM on
    ' this exe assembly! It would create InProcServer32 entries 
    ' which would prevent proper activation!
    '
    ' Using the list of COM object types generated during dynamic
    ' assembly loading, it registers each one for COM as served by our
    ' exe/local server, as well as registering it for ASCOM. It also
    ' adds DCOM info for the local server itself, so it can be activated
    ' via an outbound connection from TheSky.
    '
    Private Shared Sub RegisterObjects()
        If Not IsAdministrator Then
            ElevateSelf("/register")
            Return
        End If
        '
        ' If reached here, we're running elevated
        '

        Dim assy As Assembly = Assembly.GetExecutingAssembly()
        Dim attr As Attribute = Attribute.GetCustomAttribute(assy, GetType(AssemblyTitleAttribute))
        Dim assyTitle As String = DirectCast(attr, AssemblyTitleAttribute).Title
        attr = Attribute.GetCustomAttribute(assy, GetType(AssemblyDescriptionAttribute))
        Dim assyDescription As String = DirectCast(attr, AssemblyDescriptionAttribute).Description

        '
        ' Local server's DCOM/AppID information
        '
        Try
            '
            ' HKCR\APPID\appid
            '
            Using key As RegistryKey = Registry.ClassesRoot.CreateSubKey(Convert.ToString("APPID\") & s_appId)
                key.SetValue(Nothing, assyDescription)
                key.SetValue("AppID", s_appId)
                key.SetValue("AuthenticationLevel", 1, RegistryValueKind.DWord)
            End Using
            '
            ' HKCR\APPID\exename.ext
            '
            Using key As RegistryKey = Registry.ClassesRoot.CreateSubKey(String.Format("APPID\{0}", _
                Application.ExecutablePath.Substring(Application.ExecutablePath.LastIndexOf("\"c) + 1)))
                key.SetValue("AppID", s_appId)
            End Using
        Catch ex As Exception
            MessageBox.Show("Error while registering the server:" & vbCrLf & ex.ToString(), _
                            "Acme", MessageBoxButtons.OK, MessageBoxIcon.Stop)
            Return
        Finally
        
        End Try

        '
        ' For each of the driver assemblies
        '
        For Each ttype As Type In s_ComObjectTypes
            Dim bFail As Boolean = False
            Try
                '
                ' HKCR\CLSID\clsid
                '
                Dim clsid As String = Marshal.GenerateGuidForType(ttype).ToString("B")
                Dim progid As String = Marshal.GenerateProgIdForType(ttype)
                'PWGS Generate device type from the Class name
                Dim deviceType As String = ttype.Name

                Using key As RegistryKey = Registry.ClassesRoot.CreateSubKey(String.Format("CLSID\{0}", clsid))
                    key.SetValue(Nothing, progid)   ' Could be assyTitle/Desc??, but .NET components show ProgId here
                    key.SetValue("AppId", s_appId)
                    Using key2 As RegistryKey = key.CreateSubKey("Implemented Categories")
                        key2.CreateSubKey("{62C8FE65-4EBB-45e7-B440-6E39B2CDBF29}")
                    End Using
                    Using key2 As RegistryKey = key.CreateSubKey("ProgId")
                        key2.SetValue(Nothing, progid)
                    End Using
                    key.CreateSubKey("Programmable")
                    Using key2 As RegistryKey = key.CreateSubKey("LocalServer32")
                        key2.SetValue(Nothing, Application.ExecutablePath)
                    End Using
                End Using
                '
                ' HKCR\CLSID\progid
                '
                Using key As RegistryKey = Registry.ClassesRoot.CreateSubKey(progid)
                    key.SetValue(Nothing, assyTitle)
                    Using key2 As RegistryKey = key.CreateSubKey("CLSID")
                        key2.SetValue(Nothing, clsid)
                    End Using
                End Using
                '
                ' ASCOM 
                '
                assy = ttype.Assembly

                ' Pull the display name from the ServedClassName attribute.
                attr = Attribute.GetCustomAttribute(ttype, GetType(ServedClassNameAttribute))   'PWGS Changed to search type for attribute rather than assembly
                Dim chooserName As String = TryCast(attr, ServedClassNameAttribute).DisplayName
                If chooserName Is Nothing Then
                    chooserName = "MultiServer"
                End If
                Using P As New ASCOM.Utilities.Profile()
                    P.DeviceType = deviceType
                    P.Register(progid, chooserName)
                End Using
            Catch ex As Exception
                MessageBox.Show("Error while registering the server:" & vbCrLf & ex.ToString(), _
                                "Acme", MessageBoxButtons.OK, MessageBoxIcon.Stop)
                bFail = True
            Finally
            
            End Try
            If bFail Then
                Exit For
            End If
        Next
    End Sub

    '
    ' Remove all traces of this from the registry. 
    '
    ' **TODO** If the above does AppID/DCOM stuff, this would have
    ' to remove that stuff too.
    '
    Private Shared Sub UnregisterObjects()
        If Not IsAdministrator Then
            ElevateSelf("/unregister")
            Return
        End If

        '
        ' Local server's DCOM/AppID information
        '
        Registry.ClassesRoot.DeleteSubKey(String.Format("APPID\{0}", s_appId), False)
        Registry.ClassesRoot.DeleteSubKey(String.Format("APPID\{0}", _
                Application.ExecutablePath.Substring(Application.ExecutablePath.LastIndexOf("\"c) + 1)), False)

        '
        ' For each of the driver assemblies
        '
        For Each ttype As Type In s_ComObjectTypes
            Dim clsid As String = Marshal.GenerateGuidForType(ttype).ToString("B")
            Dim progid As String = Marshal.GenerateProgIdForType(ttype)
            Dim deviceType As String = ttype.Name
            '
            ' Best efforts
            '
            '
            ' HKCR\progid
            '
            Registry.ClassesRoot.DeleteSubKey(String.Format("{0}\CLSID", progid), False)
            Registry.ClassesRoot.DeleteSubKey(progid, False)
            '
            ' HKCR\CLSID\clsid
            '
            Registry.ClassesRoot.DeleteSubKey(String.Format("CLSID\{0}\Implemented Categories\{{62C8FE65-4EBB-45e7-B440-6E39B2CDBF29}}", clsid), False)
            Registry.ClassesRoot.DeleteSubKey(String.Format("CLSID\{0}\Implemented Categories", clsid), False)
            Registry.ClassesRoot.DeleteSubKey(String.Format("CLSID\{0}\ProgId", clsid), False)
            Registry.ClassesRoot.DeleteSubKey(String.Format("CLSID\{0}\LocalServer32", clsid), False)
            Registry.ClassesRoot.DeleteSubKey(String.Format("CLSID\{0}\Programmable", clsid), False)
            Registry.ClassesRoot.DeleteSubKey(String.Format("CLSID\{0}", clsid), False)
            Try
                '
                ' ASCOM
                '
                Using P As New ASCOM.Utilities.Profile()
                    P.DeviceType = deviceType
                    P.Unregister(progid)
                End Using
            Catch Ex As Exception
            End Try
        Next
    End Sub
#End Region

#Region "Class Factory Support"
    '
    ' On startup, we register the class factories of the COM objects
    ' that we serve. This requires the class factory name to be
    ' equal to the served class name & "ClassFactory".
    '
    Private Shared Function RegisterClassFactories() As Boolean
        s_ClassFactories = New ArrayList()
        For Each ttype As Type In s_ComObjectTypes
            Dim factory As New ClassFactory(ttype)
            ' Use default context & flags
            s_ClassFactories.Add(factory)
            If Not factory.RegisterClassObject() Then
                MessageBox.Show("Failed to register class factory for " & ttype.Name, _
                                "Acme", MessageBoxButtons.OK, MessageBoxIcon.Stop)
                Return False
            End If
        Next
        ClassFactory.ResumeClassObjects()                   ' Served objects now go live
        Return True
    End Function

    Private Shared Sub RevokeClassFactories()
        ClassFactory.SuspendClassObjects()                  ' Prevent race conditions
        For Each factory As ClassFactory In s_ClassFactories
            factory.RevokeClassObject()
        Next
    End Sub
#End Region

#Region "Command Line Arguments"
    '
    ' ProcessArguments() will process the command-line arguments
    ' If the return value is True, we carry on and start this application.
    ' If the return value is False, we terminate this application immediately.
    '
    Private Shared Function ProcessArguments(args As String()) As Boolean
        Dim bRet As Boolean = True

        '
        '**TODO** -Embedding is "ActiveX start". Prohibit non_AX starting?
        '
        If args.Length > 0 Then

            Select Case args(0).ToLower()
                Case "-embedding"
                    StartedByCOM = True                     ' Indicate COM started us

                '                               Emulate VB6
                Case "-register", "/register", "-regserver", "/regserver"
                    RegisterObjects()                       ' Register each served object
                    bRet = False

                '                                   Emulate VB6
                Case "-unregister", "/unregister", "-unregserver", "/unregserver"
                    UnregisterObjects()                     'Unregister each served object
                    bRet = False

                Case Else
                    MessageBox.Show("Unknown argument: " & args(0) & vbCrLf & _
                                    "Valid are : -register, -unregister and -embedding", _
                                    "Acme", MessageBoxButtons.OK, MessageBoxIcon.Exclamation)
            End Select
        Else
            StartedByCOM = False
        End If

        Return bRet
    End Function
#End Region

#Region "SERVER ENTRY POINT (Main)"
    '
    ' ==================
    ' SERVER ENTRY POINT
    ' ==================
    '
    <STAThread()> _
    Public Shared Sub Main(args As String())
        If Not LoadComObjectAssemblies() Then       ' Load served COM class assemblies, get types
            Return
        End If

        If Not ProcessArguments(args) Then          ' Register/Unregister
            Return
        End If

        ' Initialize critical member variables.
        objsInUse = 0
        serverLocks = 0
        MainThreadId = GetCurrentThreadId()
        Thread.CurrentThread.Name = "Main Thread"

        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)
        s_MainForm = New frmMain()
        If StartedByCOM Then
            s_MainForm.WindowState = FormWindowState.Minimized
        End If

        ' Register the class factories of the served objects
        RegisterClassFactories()

        ' Start up the garbage collection thread.
        Dim GarbageCollector As New GarbageCollection(1000)
        Dim GCThread As New Thread(New ThreadStart(AddressOf GarbageCollector.GCWatch))
        GCThread.Name = "Garbage Collection Thread"
        GCThread.Start()

        '
        ' Start the message loop. This serializes incoming calls to our
        ' served COM objects, making this act like the VB6 equivalent!
        '
        Try
            Application.Run(s_MainForm)
        Finally
            ' Revoke the class factories immediately.
            ' Don't wait until the thread has stopped before
            ' we perform revocation!!!
            RevokeClassFactories()

            ' Now stop the Garbage Collector thread.
            GarbageCollector.StopThread()
            GarbageCollector.WaitForThreadToStop()
        End Try
    End Sub
#End Region
End Class
