resource "kubernetes_namespace" "estimations" {
  metadata {
    annotations = {
      name = "estimations"
    }

    name = "estimations"
  }
}

resource "kubernetes_namespace" "external" {
  metadata {
    annotations = {
      name = "external"
    }

    name = "external"
  }
}