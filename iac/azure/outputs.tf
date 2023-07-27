output "TF_OUT_ACR_LOGIN_SERVER" {
  value     = azurerm_container_registry.acr.login_server
}

output "TF_OUT_ACR_USERNAME" {
  value     = azurerm_container_registry.acr.admin_username
}

output "TF_OUT_ACR_PASSWORD" {
  value     = azurerm_container_registry.acr.admin_password
  sensitive = true
}

output "TF_OUT_STORAGE_CONNECTION_STRING" {
  value     = azurerm_storage_account.storage.primary_connection_string
  sensitive = true
}

output "TF_OUT_APM_CONNECTION_STRING" {
  value     = azurerm_application_insights.apm.connection_string
  sensitive = true
}

output "TF_OUT_SERVICE_BUS_CONNECTION_STRING" {
  value     = azurerm_servicebus_namespace.bus.default_primary_connection_string
  sensitive = true
}