Get-ChildItem .\bin\Debug\netcoreapp3.1\ | ForEach-Object { Remove-Item -Recurse -Force $_ }