@echo off
rem Sets Legacy Paint Dark Mode as the default editor for image files (Right-click -> Edit) and hides "Edit with Paint 3D".
rem Builds first if needed. Restore original Paint anytime with "Revert to Windows Paint.cmd".
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -Shell Apply
