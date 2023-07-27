# 01.- Create the Azure infrastructure
$terraform_version = terraform version
Write-Output $terraform_version
terraform -chdir=iac init -reconfigure -upgrade
terraform -chdir=iac apply -auto-approve

# # 02.- Create the docker images and push them to the created Azure Container Registry
# $registry = terraform -chdir=iac/azure output -raw TF_OUT_ACR_LOGIN_SERVER
# $registry_username = terraform -chdir=iac/azure output -raw TF_OUT_ACR_USERNAME
# $registry_password = terraform -chdir=iac/azure output -raw TF_OUT_ACR_PASSWORD
# docker login $registry -u $registry_username -p $registry_password

# ## Application components
# docker build `
#   -f .\src\Application\Acheve.Application.Api\Dockerfile `
#   -t $registry/application-api:latest `
#   .
# docker push $registry/application-api:latest

# docker build `
#   -f .\src\Application\Acheve.Application.CallbackNotifier\Dockerfile `
#   -t $registry/application-callback-notifier:latest `
#   .
# docker push $registry/application-callback-notifier:latest

# docker build `
#   -f .\src\Application\Acheve.Application.EstimationProcessor\Dockerfile `
#   -t $registry/application-estimation-processor:latest `
#   .
# docker push $registry/application-estimation-processor:latest

# docker build `
#   -f .\src\Application\Acheve.Application.ExternalImageProcessor\Dockerfile `
#   -t $registry/application-image-processor:latest `
#   .
# docker push $registry/application-image-processor:latest

# docker build `
#   -f .\src\Application\Acheve.Application.ImageDownloadsProcessor\Dockerfile `
#   -t $registry/application-image-downloader:latest `
#   .
# docker push $registry/application-image-downloader:latest

# docker build `
#   -f .\src\Application\Acheve.Application.ProcessManager\Dockerfile `
#   -t $registry/application-process-manager:latest `
#   .
# docker push $registry/application-process-manager:latest

# docker build `
#   -f .\src\Application\Acheve.Application.StateHolder\Dockerfile `
#   -t $registry/application-state-holder:latest `
#   .
# docker push $registry/application-state-holder:latest

# ## External services
# docker build `
#   -f .\src\External\Acheve.External.Estimations\Dockerfile `
#   -t $registry/external-estimations-service:latest `
#   .
# docker push $registry/external-estimations-service:latest

# docker build `
#   -f .\src\External\Acheve.External.ImageProcess\Dockerfile `
#   -t $registry/external-image-processor-service:latest `
#   .
# docker push $registry/external-image-processor-service:latest

# docker build `
#   -f .\src\External\Acheve.External.Images\Dockerfile `
#   -t $registry/external-images:latest `
#   .
# docker push $registry/external-images:latest

# docker build `
#   -f .\src\External\Acheve.External.Notifications\Dockerfile `
#   -t $registry/external-notifications-service:latest `
#   .
# docker push $registry/external-notifications-service:latest