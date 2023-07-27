resource "azurerm_container_registry" "acr" {
  resource_group_name = azurerm_resource_group.rg.name
  location            = azurerm_resource_group.rg.location
  name                = "rebusdemoacr"

  admin_enabled                 = true
  anonymous_pull_enabled        = false
  data_endpoint_enabled         = false
  export_policy_enabled         = true
  network_rule_bypass_option    = "AzureServices"
  network_rule_set              = []
  public_network_access_enabled = true
  quarantine_policy_enabled     = false
  retention_policy = [{
    days    = 7
    enabled = false
  }]
  sku  = "Standard"
  tags = {
    scope = "demo"
  }
  trust_policy = [{
    enabled = false
  }]
  zone_redundancy_enabled = false
}