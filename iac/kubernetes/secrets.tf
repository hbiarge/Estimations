resource "kubernetes_secret" "estimations_common_secrets" {
  metadata {
    name = "common"
    namespace = kubernetes_namespace.estimations.metadata.0.name
  }

  data = {
    "secrets.json" = jsonencode({
      "ConnectionStrings" = {
        "Storage" = var.storage_connection_string
        "ServiceBus" = var.service_bus_connection_string
        "ApplicationInsights" = var.apm_connection_string
      }
    })
  }

  type = "Opaque"
}