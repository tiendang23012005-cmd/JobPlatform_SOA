$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root 'src/Services/Payment.Api/bin/Release/net8.0'
$payment = Join-Path $output 'Payment.Api.dll'
if (!(Test-Path -LiteralPath $payment)) { throw 'Cần build Payment.Api ở cấu hình Release trước.' }

$dotnetRoot = Split-Path -Parent (Get-Command dotnet).Source
$sdk = ((& dotnet --version) -join '').Trim()
$compiler = Join-Path $dotnetRoot "sdk/$sdk/Roslyn/bincore/csc.dll"
$references = foreach ($packName in 'Microsoft.NETCore.App.Ref', 'Microsoft.AspNetCore.App.Ref') {
    $referencePack = Get-ChildItem (Join-Path $dotnetRoot "packs/$packName") -Directory |
        Where-Object Name -like '8.0.*' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    if (!$referencePack) { throw "Cần targeting pack .NET 8 cho $packName để chạy kiểm thử." }
    Get-ChildItem (Join-Path $referencePack.FullName 'ref/net8.0') -Filter '*.dll' |
        ForEach-Object { '-r:"' + $_.FullName + '"' }
}
$references += ('-r:"' + $payment + '"'), ('-r:"' + (Join-Path $output 'Shared.Contracts.dll') + '"')
$testAssembly = Join-Path $output 'Wallet.Unit.dll'
$responseFile = Join-Path $output 'Wallet.Unit.rsp'
[IO.File]::WriteAllLines($responseFile, [string[]]$references, [Text.UTF8Encoding]::new($false))
try {
    & dotnet $compiler -nologo -nullable:enable -target:exe "-out:$testAssembly" "@$responseFile" (Join-Path $PSScriptRoot 'Wallet.Unit.cs')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally { Remove-Item -LiteralPath $responseFile }
& dotnet exec --runtimeconfig (Join-Path $output 'Payment.Api.runtimeconfig.json') --depsfile (Join-Path $output 'Payment.Api.deps.json') $testAssembly
exit $LASTEXITCODE
