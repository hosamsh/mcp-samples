param (

    [Parameter(Mandatory=$True, HelpMessage='Tenant ID')]
    [string]$TENANT,

    [Parameter(Mandatory=$True, HelpMessage='Azure Subscription ID')]
    [string]$SUBSCRIPTION,
    
    [Parameter(Mandatory=$True, HelpMessage='Resource Prefix used for naming the resources')]
    [string]$RESOURCE_PREFIX
)

## usage: .\cleanup.ps1 -TENANT 638eb391-a880-41f2-991a-274ff9c09f9b -SUBSCRIPTION a01f390d-3622-4cea-b985-2f43f5334f6e -RESOURCE_PREFIX "T1PO"
try {
    az account set --subscription $SUBSCRIPTION
    Write-Host "User is already signed in..."
}
catch {    
    Write-Host "Logging into Azure..." -ForegroundColor Yellow
    Write-Host "Tenant id $TENANT" -ForegroundColor Green
    az login --tenant $TENANT 
    Write-Host "setting subscription $SUBSCRIPTION" -ForegroundColor Green
    az account set --subscription $SUBSCRIPTION
}

$USER_EMAIL = az ad signed-in-user show --query "userPrincipalName" -o tsv
Write-Host "Welcome: $USER_EMAIL!" -ForegroundColor Cyan
Start-Sleep -Milliseconds 100

$RESOURCE_GROUP_NAME = $RESOURCE_PREFIX + "2RG"

Write-Host "Starting cleanup process..." -ForegroundColor Yellow

Write-Host "Deleting resource group $RESOURCE_GROUP_NAME..." -ForegroundColor Yellow
az group delete --name $RESOURCE_GROUP_NAME --no-wait --yes

# Delete App Registration
Write-Host "Deleting app registration..." -ForegroundColor Yellow
$APP_REG_NAME = $RESOURCE_PREFIX + "2AppReg"
$APP_REG_ID = az ad app list --display-name $APP_REG_NAME --query "[].appId" --output tsv
if ($APP_REG_ID) {
    az ad app delete --id $APP_REG_ID
    Write-Host "Deleted App Registration: $APP_REG_NAME" -ForegroundColor Green
} else {
    Write-Host "No App Registration found with name: $APP_REG_NAME" -ForegroundColor Cyan
}

Write-Host "Cleanup process completed!" -ForegroundColor Green