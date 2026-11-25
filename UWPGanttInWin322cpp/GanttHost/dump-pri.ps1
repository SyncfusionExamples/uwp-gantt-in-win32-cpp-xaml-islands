param([string]$Pri)
$mp = (Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin\*\x64\makepri.exe' | Sort-Object FullName | Select-Object -Last 1).FullName
$o = Join-Path $env:TEMP 'pridump.xml'
& $mp dump /if $Pri /of $o /o | Out-Null
$x = Get-Content $o -Raw
"ResourceMaps:"
[regex]::Matches($x, '<ResourceMap[^>]*name="([^"]*)"') | ForEach-Object { '  ' + $_.Groups[1].Value } | Select-Object -First 15
"Sample named resources:"
[regex]::Matches($x, '<NamedResource name="([^"]*)"') | ForEach-Object { '  ' + $_.Groups[1].Value } | Select-Object -First 8
