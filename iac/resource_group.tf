import {
  to = azurerm_resource_group.rg
  id = "/subscriptions/9586abf0-77e3-4f0d-96e7-26ece6da09f3/resourceGroups/rebus-demo"
}

resource "azurerm_resource_group" "rg" {
  location = var.default_region
  name     = "rebus-demo"
  tags = {
    scope = "demo"
  }
}