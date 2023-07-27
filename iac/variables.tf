locals {
  resource_group_name = "rebus-demo-rg"
  cluster_name        = "rebus-demo-aks"
}

variable "default_region" {
  type    = string
  default = "westeurope"
}
