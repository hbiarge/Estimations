resource "kubernetes_config_map" "estimations_common_config" {
  metadata {
    name = "common"
    namespace = kubernetes_namespace.estimations.metadata.0.name
  }

  data = {
    api_host             = "myhost:443"
    db_host              = "dbhost:5432"
  }
}