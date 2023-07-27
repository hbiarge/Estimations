# Configure the Azure provider
terraform {
  required_version = "1.5.3"
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "3.66.0"
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
