resource "azurerm_resource_group" "rg" {
  location = var.default_region
  name     = var.resource_group_name
  tags = {
    scope = "demo"
  }
}