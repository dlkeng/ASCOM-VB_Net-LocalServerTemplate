Imports System
Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.Drawing
Imports System.Text
Imports System.Windows.Forms

Public Partial Class frmMain
    Inherits Form
    Private Delegate Sub SetTextCallback(text As String)

    Public Sub New()
        InitializeComponent()
    End Sub

End Class
