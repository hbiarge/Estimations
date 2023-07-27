terraform {
  required_version = "1.5.3"
  required_providers {
    local = {
      source  = "hashicorp/local"
      version = "2.4.0"
    }
    kubernetes = {
      source  = "hashicorp/kubernetes"
      version = "2.22.0"
    }
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "3.66.0"
    }
    helm = {
      source  = "hashicorp/helm"
      version = "2.10.1"
    }
  }
}

provider "azurerm" {
  features {
    api_management {
      purge_soft_delete_on_destroy = true
      recover_soft_deleted         = true
    }

    app_configuration {
      purge_soft_delete_on_destroy = true
      recover_soft_deleted         = true
    }

    application_insights {
      disable_generated_rule = false
    }

    cognitive_account {
      purge_soft_delete_on_destroy = true
    }

    key_vault {
      purge_soft_delete_on_destroy    = true
      recover_soft_deleted_key_vaults = true
    }

    log_analytics_workspace {
      permanently_delete_on_destroy = true
    }

    managed_disk {
      expand_without_downtime = true
    }

    resource_group {
      prevent_deletion_if_contains_resources = false
    }

    template_deployment {
      delete_nested_items_during_deletion = true
    }

    virtual_machine {
      delete_os_disk_on_deletion     = true
      graceful_shutdown              = false
      skip_shutdown_and_force_delete = false
    }

    virtual_machine_scale_set {
      force_delete                  = false
      roll_instances_when_required  = true
      scale_to_zero_before_deletion = true
    }
  }
}

data "azurerm_kubernetes_cluster" "aks" {
  depends_on          = [module.azure] # refresh cluster state before reading
  name                = local.cluster_name
  resource_group_name = local.resource_group_name
}

resource "local_file" "kubeconfig" {
  content  = data.azurerm_kubernetes_cluster.aks.kube_config_raw
  filename = "${path.module}/kubeconfig"
}

provider "kubernetes" {
  host                   = data.azurerm_kubernetes_cluster.aks.kube_config.0.host
  client_certificate     = base64decode(data.azurerm_kubernetes_cluster.aks.kube_config.0.client_certificate)
  client_key             = base64decode(data.azurerm_kubernetes_cluster.aks.kube_config.0.client_key)
  cluster_ca_certificate = base64decode(data.azurerm_kubernetes_cluster.aks.kube_config.0.cluster_ca_certificate)
}

provider "helm" {
  kubernetes {
    host                   = data.azurerm_kubernetes_cluster.aks.kube_config.0.host
    client_certificate     = base64decode(data.azurerm_kubernetes_cluster.aks.kube_config.0.client_certificate)
    client_key             = base64decode(data.azurerm_kubernetes_cluster.aks.kube_config.0.client_key)
    cluster_ca_certificate = base64decode(data.azurerm_kubernetes_cluster.aks.kube_config.0.cluster_ca_certificate)
  }
}

module "azure" {
  source = "./azure"
  # Variables
  default_region      = var.default_region
  resource_group_name = local.resource_group_name
  cluster_name        = local.cluster_name
}

module "kubernetes" {
  depends_on = [module.azure]
  source     = "./kubernetes"
  # Variables
  storage_connection_string     = module.azure.TF_OUT_STORAGE_CONNECTION_STRING
  service_bus_connection_string = module.azure.TF_OUT_SERVICE_BUS_CONNECTION_STRING
  apm_connection_string         = module.azure.TF_OUT_APM_CONNECTION_STRING
  # cluster_name = local.cluster_name
  # kubeconfig   = data.azurerm_kubernetes_cluster.aks.kube_config_raw
}