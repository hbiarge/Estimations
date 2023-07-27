data "azurerm_kubernetes_service_versions" "current" {
  location = azurerm_resource_group.rg.location
  include_preview = false
}

resource "azurerm_kubernetes_cluster" "aks" {
  resource_group_name = azurerm_resource_group.rg.name
  location            = azurerm_resource_group.rg.location
  name                = var.cluster_name

  automatic_channel_upgrade           = "patch"
  azure_policy_enabled                = false
  custom_ca_trust_certificates_base64 = []
  disk_encryption_set_id              = null
  dns_prefix                          = "rebus-demo-aks-dns"
  dns_prefix_private_cluster          = null
  edge_zone                           = null
  http_application_routing_enabled    = false
  image_cleaner_enabled               = null
  image_cleaner_interval_hours        = null
  kubernetes_version                  = data.azurerm_kubernetes_service_versions.current.latest_version
  local_account_disabled              = false
  node_os_channel_upgrade             = null
  oidc_issuer_enabled                 = false
  open_service_mesh_enabled           = false
  private_cluster_enabled             = false
  private_cluster_public_fqdn_enabled = false
  private_dns_zone_id                 = null
  public_network_access_enabled       = true
  role_based_access_control_enabled = true
  run_command_enabled               = true
  sku_tier                          = "Free"
  tags                              = {}
  workload_identity_enabled         = false
  auto_scaler_profile {
    balance_similar_node_groups      = false
    empty_bulk_delete_max            = "10"
    expander                         = "random"
    max_graceful_termination_sec     = "600"
    max_node_provisioning_time       = "15m"
    max_unready_nodes                = 3
    max_unready_percentage           = 45
    new_pod_scale_up_delay           = "0s"
    scale_down_delay_after_add       = "10m"
    scale_down_delay_after_delete    = "10s"
    scale_down_delay_after_failure   = "3m"
    scale_down_unneeded              = "10m"
    scale_down_unready               = "20m"
    scale_down_utilization_threshold = "0.5"
    scan_interval                    = "10s"
    skip_nodes_with_local_storage    = false
    skip_nodes_with_system_pods      = true
  }
  default_node_pool {
    capacity_reservation_group_id = null
    custom_ca_trust_enabled       = false
    enable_auto_scaling           = true
    enable_host_encryption        = false
    enable_node_public_ip         = false
    fips_enabled                  = false
    host_group_id                 = null
    kubelet_disk_type             = "OS"
    max_count                     = 5
    max_pods                      = 110
    message_of_the_day            = null
    min_count                     = 1
    name                          = "agentpool"
    node_count                    = 1
    node_labels                   = {}
    node_public_ip_prefix_id      = null
    node_taints                   = []
    only_critical_addons_enabled  = false
    orchestrator_version          = data.azurerm_kubernetes_service_versions.current.latest_version
    os_disk_size_gb               = 128
    os_disk_type                  = "Managed"
    os_sku                        = "Ubuntu"
    pod_subnet_id                 = null
    proximity_placement_group_id  = null
    scale_down_mode               = "Delete"
    tags                          = {}
    temporary_name_for_rotation   = null
    type                          = "VirtualMachineScaleSets"
    ultra_ssd_enabled             = false
    vm_size                       = "Standard_B4ms"
    vnet_subnet_id                = null
    workload_runtime              = null
    zones                         = []
  }
  identity {
    identity_ids = []
    type         = "SystemAssigned"
  }
  network_profile {
    dns_service_ip      = "10.0.0.10"
    ebpf_data_plane     = null
    ip_versions         = ["IPv4"]
    load_balancer_sku   = "standard"
    network_mode        = null
    network_plugin      = "kubenet"
    network_plugin_mode = null
    network_policy      = null
    outbound_type       = "loadBalancer"
    pod_cidr            = "10.244.0.0/16"
    pod_cidrs           = ["10.244.0.0/16"]
    service_cidr        = "10.0.0.0/16"
    service_cidrs       = ["10.0.0.0/16"]
    load_balancer_profile {
      managed_outbound_ip_count   = 1
      outbound_ports_allocated    = 0
    }
  }
}

# add the role to the identity the kubernetes cluster was assigned
resource "azurerm_role_assignment" "aks_to_acr" {
  scope                = azurerm_container_registry.acr.id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_kubernetes_cluster.aks.kubelet_identity[0].object_id
}