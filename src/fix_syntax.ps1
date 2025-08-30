$files = Get-ChildItem *.cs

foreach ($file in $files) {
    $content = Get-Content $file.FullName -Raw
    
    # Fix missing opening brace after namespace
    $content = $content -replace "namespace SOSDM(\s*)(//.*)?(\s*)([^{])", "namespace SOSDM`$1`$2`$3{`r`n`$4"
    
    # Add closing brace if missing
    if ($content -notmatch "}\s*$") {
        $content = $content.TrimEnd() + "`r`n}"
    }
    
    $content | Set-Content $file.FullName -Encoding UTF8
    Write-Host "Fixed: $($file.Name)"
}