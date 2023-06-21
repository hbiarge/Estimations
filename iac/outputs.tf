output "TF_OUT_STORAGE_CONNECTION_STRING" {
  value = azurerm_storage_account.storage.primary_connection_string
  sensitive = true
}

output "TF_OUT_APM_CONNECTION_STRING" {
  value = azurerm_application_insights.apm.connection_string
  sensitive = true
}

output "TF_OUT_SERVICE_BUS_CONNECTION_STRING" {
  value = azurerm_servicebus_namespace.bus.default_primary_connection_string
  sensitive = true
}