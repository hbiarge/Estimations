resource "azurerm_servicebus_namespace" "bus" {
  resource_group_name = azurerm_resource_group.rg.name
  location            = azurerm_resource_group.rg.location
  name                = "sb-rebus-demo"

  capacity                      = 0
  local_auth_enabled            = true
  minimum_tls_version           = "1.2"
  public_network_access_enabled = true
  sku                           = "Standard"
  tags = {
    scope = "demo"
  }
  zone_redundant = false
}
