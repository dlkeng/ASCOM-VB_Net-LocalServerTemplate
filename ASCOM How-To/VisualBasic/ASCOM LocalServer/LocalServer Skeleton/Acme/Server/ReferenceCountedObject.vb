Imports System
Imports System.Runtime.InteropServices

<ComVisible(False)> _
Public Class ReferenceCountedObjectBase
    Public Sub New()
        ' We increment the global count of objects.
        Server.CountObject()
    End Sub

    Protected Overrides Sub Finalize()
        Try
            ' We decrement the global count of objects.
            Server.UncountObject()
            ' We then immediately test to see if we the conditions
            ' are right to attempt to terminate this server application.
            Server.ExitIf()
        Finally
            MyBase.Finalize()
        End Try
    End Sub
End Class
