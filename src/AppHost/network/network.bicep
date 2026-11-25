// The network the published application runs in (#149, docs/adr/0038), written by hand and added by
// src/AppHost/Wiring/PrivateNetwork.cs: the Container Apps environment is injected into aca-subnet, and the PostgreSQL
// server and the Key Vault are reached through private endpoints in private-endpoints alone, their names resolving to the
// endpoints' addresses through the private DNS zones linked to the network.

@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

@description('The PostgreSQL Flexible Server the network reaches privately.')
param postgres_outputs_id string

@description('The Key Vault the network reaches privately.')
param secrets_outputs_id string

// One entry per resource reached privately: the sub-resource its endpoint connects to and the zone its name resolves in.
var targets = [
  {
    name: 'postgres'
    id: postgres_outputs_id
    groupId: 'postgresqlServer'
    zone: 'privatelink.postgres.database.azure.com'
  }
  {
    name: 'secrets'
    id: secrets_outputs_id
    groupId: 'vault'
    zone: 'privatelink.vaultcore.azure.net'
  }
]

resource vnet 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: take('vnet-${uniqueString(resourceGroup().id)}', 64)
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: [
        '10.0.0.0/16'
      ]
    }
    subnets: [
      {
        // A workload-profiles environment needs a /27 at least; a /23 leaves its replicas room to grow.
        name: 'aca-subnet'
        properties: {
          addressPrefix: '10.0.0.0/23'
          delegations: [
            {
              name: 'Microsoft.App/environments'
              properties: {
                serviceName: 'Microsoft.App/environments'
              }
            }
          ]
        }
      }
      {
        name: 'private-endpoints'
        properties: {
          addressPrefix: '10.0.2.0/24'
        }
      }
    ]
  }
  tags: {
    'aspire-resource-name': 'network'
  }
}

resource zones 'Microsoft.Network/privateDnsZones@2024-06-01' = [for target in targets: {
  name: target.zone
  location: 'global'
}]

resource links 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = [for (target, i) in targets: {
  parent: zones[i]
  name: 'vnet'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: vnet.id
    }
  }
}]

resource endpoints 'Microsoft.Network/privateEndpoints@2024-05-01' = [for target in targets: {
  name: take('${target.name}-pe-${uniqueString(resourceGroup().id)}', 64)
  location: location
  properties: {
    subnet: {
      // The subnets in the order declared above: aca-subnet, then private-endpoints.
      id: vnet.properties.subnets[1].id
    }
    privateLinkServiceConnections: [
      {
        name: target.name
        properties: {
          privateLinkServiceId: target.id
          groupIds: [
            target.groupId
          ]
        }
      }
    ]
  }
}]

resource zoneGroups 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-05-01' = [for (target, i) in targets: {
  parent: endpoints[i]
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: target.name
        properties: {
          privateDnsZoneId: zones[i].id
        }
      }
    ]
  }
}]

output acaSubnetId string = vnet.properties.subnets[0].id
