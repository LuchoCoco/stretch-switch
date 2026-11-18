@echo off
rem Compile Stretch Switch avec le compilateur C# fourni avec Windows (aucune installation requise)
cd /d "%~dp0"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /optimize+ /codepage:65001 ^
  /out:StretchSwitch.exe /win32manifest:src\app.manifest ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Xml.dll /r:System.Core.dll ^
  src\*.cs
if errorlevel 1 (echo ECHEC de la compilation & exit /b 1)
echo OK : StretchSwitch.exe cree.
