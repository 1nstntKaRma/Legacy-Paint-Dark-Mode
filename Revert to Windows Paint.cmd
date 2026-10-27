@echo off
rem Restores the original Windows Paint for Right-click -> Edit on image files.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -NoBuild -Shell Revert
