$terraform_version = terraform version
Write-Output $terraform_version
terraform -chdir=iac destroy -auto-approve