#Requires -Version 5.0

<#
.SYNOPSIS
Builds and deploys the MCP Blob Server application to an Azure Web App.

.DESCRIPTION
This script performs the following operations:
1. Builds the MCP Blob Server project in Release configuration
2. Creates a deployment package (zip file)
3. Deploys the package to an Azure Web App
4. Displays deployment status and access URL

.PARAMETER ResourceGroupName
The name of the Azure Resource Group containing the Web App.

.PARAMETER WebAppName
The name of the Azure Web App to deploy to.

.PARAMETER Configuration
(Optional) The build configuration to use. Default: Release.

.PARAMETER Runtime
(Optional) The runtime identifier to build for. Default: win-x86.

.PARAMETER SelfContained
(Optional) Whether to create a self-contained deployment. Default: false.

.PARAMETER OutputPath
(Optional) The folder where to output the published files. Default: ./publish.

.PARAMETER PackagePath
(Optional) The path for the generated zip package. Default: ./package.zip.

.EXAMPLE
.\deploy.ps1 -ResourceGroupName "MyResourceGroup" -WebAppName "MyWebApp"

.EXAMPLE
.\deploy.ps1 -ResourceGroupName "MyResourceGroup" -WebAppName "MyWebApp" -Configuration "Debug" -Runtime "win-x64" -SelfContained $true
#>

[CmdletBinding()]
param (
    [Parameter(Mandatory=$true, HelpMessage="Name of the Azure Resource Group containing the Web App")]
    [string]$ResourceGroupName,
    
    [Parameter(Mandatory=$true, HelpMessage="Name of the Azure Web App to deploy to")]
    [string]$WebAppName,
    
    [Parameter(Mandatory=$false, HelpMessage="Build configuration to use")]
    [string]$Configuration = "Release",
    
    [Parameter(Mandatory=$false, HelpMessage="Runtime identifier to build for")]
    [string]$Runtime = "win-x86",
    
    [Parameter(Mandatory=$false, HelpMessage="Create a self-contained deployment")]
    [bool]$SelfContained = $false,
    
    [Parameter(Mandatory=$false, HelpMessage="Folder where to output the published files")]
    [string]$OutputPath = "./publish",
    
    [Parameter(Mandatory=$false, HelpMessage="Path for the generated zip package")]
    [string]$PackagePath = "./package.zip"
)

# Function to check if a command is available
function Test-CommandExists {
    param ($command)
    $oldPreference = $ErrorActionPreference
    $ErrorActionPreference = 'stop'
    try { if (Get-Command $command) { $true } }
    catch { $false }
    finally { $ErrorActionPreference = $oldPreference }
}

# Function to check if user is logged in to Azure
function Test-AzureLoggedIn {
    try {
        $context = az account show 2>$null
        return ($null -ne $context)
    }
    catch {
        return $false
    }
}

# Start the deployment process
Write-Host "Starting deployment process..." -ForegroundColor Green

# Check if dotnet CLI is available
if (-not (Test-CommandExists "dotnet")) {
    Write-Host "Error: .NET SDK not found. Please install the .NET SDK and try again." -ForegroundColor Red
    exit 1
}

# Check if az CLI is available
if (-not (Test-CommandExists "az")) {
    Write-Host "Error: Azure CLI not found. Please install the Azure CLI and try again." -ForegroundColor Red
    exit 1
}

# Check if logged in to Azure
if (-not (Test-AzureLoggedIn)) {
    Write-Host "Not logged in to Azure. Initiating login..." -ForegroundColor Yellow
    az login
    
    # Verify login was successful
    if (-not (Test-AzureLoggedIn)) {
        Write-Host "Error: Failed to login to Azure. Please login manually using 'az login' and try again." -ForegroundColor Red
        exit 1
    }
}

# Set current directory to project root
try {
    $scriptDir = $PSScriptRoot
    $projectDir = Join-Path -Path $scriptDir -ChildPath ".."
    
    # Navigate to project directory
    Push-Location -Path $projectDir
    
    Write-Host "Publishing project..." -ForegroundColor Yellow
    
    # Create output directory if it doesn't exist
    if (-not (Test-Path -Path $OutputPath)) {
        New-Item -Path $OutputPath -ItemType Directory -Force | Out-Null
    } else {
        # Clean the output directory
        Get-ChildItem -Path $OutputPath -Recurse | Remove-Item -Force -Recurse
    }
    
    # Build and publish the project
    $selfContainedFlag = 
    if ($SelfContained) { "--self-contained true" } 
    else { "--self-contained false" }
    $publishCommand = "dotnet publish mcp-blob-server.csproj --configuration $Configuration --runtime $Runtime $selfContainedFlag --output $OutputPath"
    
    Write-Host "Running: $publishCommand" -ForegroundColor Cyan
    Invoke-Expression $publishCommand
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Error: Failed to publish the project." -ForegroundColor Red
        exit 1
    }
    
    # Create zip package
    Write-Host "Creating deployment package..." -ForegroundColor Yellow
    
    # Remove old package if it exists
    if (Test-Path -Path $PackagePath) {
        Remove-Item -Path $PackagePath -Force
    }
    
    Compress-Archive -Path "$OutputPath\*" -DestinationPath $PackagePath -Force
    
    if (-not (Test-Path -Path $PackagePath)) {
        Write-Host "Error: Failed to create deployment package." -ForegroundColor Red
        exit 1
    }
    
    # Deploy to Azure
    Write-Host "Deploying to Azure Web App: $WebAppName..." -ForegroundColor Yellow
    
    # Check if web app exists
    $webAppExists = az webapp show --resource-group $ResourceGroupName --name $WebAppName 2>$null
    
    if ($null -eq $webAppExists) {
        Write-Host "Error: Web App '$WebAppName' not found in Resource Group '$ResourceGroupName'." -ForegroundColor Red
        exit 1
    }
    
    # Deploy the package
    az webapp deployment source config-zip --resource-group $ResourceGroupName --name $WebAppName --src $PackagePath
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Error: Failed to deploy to Azure Web App." -ForegroundColor Red
        exit 1
    }
    
    # Get the web app URL
    $webAppUrl = az webapp show --name $WebAppName --resource-group $ResourceGroupName --query "defaultHostName" -o tsv
    
    # Display success message
    Write-Host "Deployment completed successfully!" -ForegroundColor Green
    Write-Host "You can access the web app at: https://$webAppUrl" -ForegroundColor Green
}
catch {
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
finally {
    # Restore original directory
    Pop-Location
}