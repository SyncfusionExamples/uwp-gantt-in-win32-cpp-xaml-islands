# Copies the implementation facade assemblies (from the .NET SDK Build.Extensions package) next to the exe and writes GanttHost.exe.config with a
# binding redirect for each one. Syncfusion's UWP assemblies reference netstandard-era facade versions
# (System.Runtime 4.2.1.0, ...) that the .NET Framework GAC does not provide.
param([Parameter(Mandatory)][string]$Out)

$facades = (Get-Item 'C:\Program Files\dotnet\sdk\*\Microsoft\Microsoft.NET.Build.Extensions\net461\lib' | Sort-Object FullName | Select-Object -Last 1).FullName

# Build a System.Runtime 4.2.1.0 facade: netstandard.dll 2.0 already type-forwards everything
# (including TaskAwaiter<T>) to mscorlib/System, so rename it and bump the version with Mono.Cecil.
# It is delay-signed, so it is written as System.Runtime.facade.bin and loaded from bytes by Bridge's
# AssemblyResolve handler. Loading from bytes skips strong-name validation, so no `sn -Vr` entry is needed
# (a System.Runtime.dll next to the exe fails with 0x8013141A on machines without that entry).
$cecil = Join-Path $env:TEMP 'GanttHost-cecil\Mono.Cecil.dll'
if (-not (Test-Path $cecil)) {
    $zip = Join-Path $env:TEMP 'GanttHost-cecil.zip'
    Invoke-WebRequest 'https://api.nuget.org/v3-flatcontainer/mono.cecil/0.11.6/mono.cecil.0.11.6.nupkg' -OutFile $zip
    Expand-Archive $zip (Join-Path $env:TEMP 'GanttHost-cecil-pkg') -Force
    New-Item -ItemType Directory -Force (Split-Path $cecil) | Out-Null
    Copy-Item (Join-Path $env:TEMP 'GanttHost-cecil-pkg\lib\net40\Mono.Cecil.dll') $cecil
}
Add-Type -Path $cecil
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $facades 'netstandard.dll'))
$asm.Name.Name = 'System.Runtime'
$asm.Name.Version = [Version]'4.2.1.0'
$real = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $facades 'System.Runtime.dll'))
$asm.Name.HasPublicKey = $true
$asm.Name.PublicKey = $real.Name.PublicKey
$asm.MainModule.Attributes = $asm.MainModule.Attributes -band (-bnot [Mono.Cecil.ModuleAttributes]::StrongNameSigned)
$asm.MainModule.Name = 'System.Runtime.dll'
$asm.Write((Join-Path $Out 'System.Runtime.facade.bin'))

$redirects = foreach ($f in Get-ChildItem $facades -Filter System.*.dll | Where-Object Name -ne 'System.Runtime.dll') {
    # System.Runtime is deliberately not shipped: Bridge's AssemblyResolve handler maps it to mscorlib.
    Copy-Item $f.FullName $Out -Force
    $n = [Reflection.AssemblyName]::GetAssemblyName($f.FullName)
    $tok = ($n.GetPublicKeyToken() | ForEach-Object { $_.ToString('x2') }) -join ''
    "      <dependentAssembly><assemblyIdentity name=`"$($n.Name)`" publicKeyToken=`"$tok`" culture=`"neutral`" /><bindingRedirect oldVersion=`"0.0.0.0-65535.65535.65535.65535`" newVersion=`"$($n.Version)`" /></dependentAssembly>"
}

$xml = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <startup useLegacyV2RuntimeActivationPolicy="true">
    <supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8" />
  </startup>
  <runtime>
    <loadFromRemoteSources enabled="true" />
    <assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1">
      <dependentAssembly><assemblyIdentity name="Syncfusion.Licensing" publicKeyToken="632609b4d040f6b4" culture="neutral" /><bindingRedirect oldVersion="0.0.0.0-35.2462.13.0" newVersion="35.2462.13.0" /></dependentAssembly>
      <dependentAssembly><assemblyIdentity name="Syncfusion.Telemetry" publicKeyToken="3d67ed1f87d44c89" culture="neutral" /><bindingRedirect oldVersion="0.0.0.0-35.2462.13.0" newVersion="35.2462.13.0" /></dependentAssembly>
$($redirects -join "`r`n")
    </assemblyBinding>
  </runtime>
</configuration>
"@
Set-Content -Path (Join-Path $Out 'GanttHost.exe.config') -Value $xml -Encoding UTF8

# XAML resources (Themes\Generic.xaml etc.) and a merged resources.pri: unpackaged XAML Islands apps need both
# next to the exe, otherwise the Syncfusion control templates are not found.
$pkgs = Join-Path $env:USERPROFILE '.nuget\packages'
$pri = Join-Path $env:TEMP 'GanttHost-pri'
Remove-Item $pri -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $pri | Out-Null
foreach ($p in 'sfgantt','sfgrid','sfinput','sfshared','sftabcontrol','data') {
    $lib = Join-Path $pkgs "syncfusion.$p.uwp\35.2.13\lib\uap10.0"
    Copy-Item (Join-Path $lib 'Syncfusion*') $Out -Recurse -Force -Exclude *.dll,*.xml
    Copy-Item (Join-Path $lib '*.pri') $pri
}
Copy-Item (Join-Path $Out 'Microsoft.Toolkit.Win32.UI.XamlHost.pri') $pri -ErrorAction SilentlyContinue
$makepri = (Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin\*\x64\makepri.exe' | Sort-Object FullName | Select-Object -Last 1).FullName
& $makepri createconfig /cf (Join-Path $pri 'priconfig.xml') /dq en-US /pv 10.0.0 /o | Out-Null
& $makepri new /pr $pri /cf (Join-Path $pri 'priconfig.xml') /of (Join-Path $Out 'resources.pri') /o /in GanttHost | Out-Null


