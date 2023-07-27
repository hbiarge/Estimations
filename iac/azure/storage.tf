resource "azurerm_storage_account" "storage" {
  resource_group_name = azurerm_resource_group.rg.name
  location            = azurerm_resource_group.rg.location
  name                = "rebusdemo"

  access_tier                       = "Hot"
  account_kind                      = "StorageV2"
  account_replication_type          = "LRS"
  account_tier                      = "Standard"
  allow_nested_items_to_be_public   = true
  allowed_copy_scope                = null
  cross_tenant_replication_enabled  = true
  default_to_oauth_authentication   = false
  edge_zone                         = null
  enable_https_traffic_only         = true
  infrastructure_encryption_enabled = false
  is_hns_enabled                    = false
  large_file_share_enabled          = null
  min_tls_version                   = "TLS1_2"
  nfsv3_enabled                     = false
  public_network_access_enabled     = true
  queue_encryption_key_type         = "Service"
  sftp_enabled                      = false
  shared_access_key_enabled         = true
  table_encryption_key_type         = "Service"
  tags = {
    scope = "demo"
  }
  blob_properties {
    change_feed_enabled           = false
    change_feed_retention_in_days = null
    default_service_version       = null
    last_access_time_enabled      = false
    versioning_enabled            = false
  }
  network_rules {
    bypass                     = ["AzureServices"]
    default_action             = "Allow"
    ip_rules                   = []
    virtual_network_subnet_ids = []
  }
  queue_properties {
    hour_metrics {
      enabled               = true
      include_apis          = true
      retention_policy_days = 7
      version               = "1.0"
    }
    logging {
      delete                = false
      read                  = false
      retention_policy_days = null
      version               = "1.0"
      write                 = false
    }
    minute_metrics {
      enabled               = false
      include_apis          = false
      retention_policy_days = null
      version               = "1.0"
    }
  }
  share_properties {
  }
}