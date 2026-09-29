param([switch]$SelfTest)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -Path (Join-Path $PSScriptRoot 'TimedClick.cs') -ReferencedAssemblies System.Windows.Forms,System.Drawing
if ($SelfTest) { [TimedClick.Program]::SelfTest(); exit }
[System.Windows.Forms.Application]::EnableVisualStyles()
[System.Windows.Forms.Application]::Run([TimedClick.MainForm]::new())
