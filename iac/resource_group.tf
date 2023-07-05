resource "azurerm_resource_group" "rg" {
  location = var.default_region
  name     = "rebus-demo"
  tags = {
    scope = "demo"
  }
}