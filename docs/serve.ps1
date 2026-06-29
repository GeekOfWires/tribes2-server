# Preview the docs site locally at http://127.0.0.1:4000/ (Windows / PowerShell).
#
#   powershell -ExecutionPolicy Bypass -File docs\serve.ps1
#
# Finds a Ruby install if it isn't already on PATH, then serves the site with the
# local-preview config (no /tribes2-server baseurl, so styling + links work at root).
$ErrorActionPreference = 'Stop'

if (-not (Get-Command bundle -ErrorAction SilentlyContinue)) {
    $bin = @('C:\Ruby*\bin', 'C:\tools\ruby*\bin',
             "$env:LOCALAPPDATA\Programs\Ruby*\bin",
             "$env:USERPROFILE\scoop\apps\ruby\current\bin") |
        ForEach-Object { Resolve-Path $_ -ErrorAction SilentlyContinue } |
        Where-Object { $_ -and (Test-Path (Join-Path $_.Path 'ruby.exe')) } |
        Select-Object -First 1
    if ($bin) { $env:Path = "$($bin.Path);$env:Path" }
}

if (-not (Get-Command bundle -ErrorAction SilentlyContinue)) {
    Write-Error "Ruby/Bundler not found. Install Ruby+Devkit, then 'gem install bundler' if needed."
    exit 1
}

Set-Location $PSScriptRoot
if (-not (Test-Path 'Gemfile.lock')) { bundle install }
bundle exec jekyll serve --config _config.yml,_config_dev.yml --livereload
