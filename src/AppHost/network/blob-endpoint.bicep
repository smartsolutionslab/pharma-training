// The services' private endpoint to the storage account's blobs (#149, docs/adr/0038), added by
// src/AppHost/Wiring/PrivateNetwork.cs. It is a module of its own, after the account and the network: the account comes
// after the Container Apps environment (it allows the environment's origins), and the environment after the network, so
// the network cannot wait for the account. The account keeps its public endpoint for the browsers' SAS uploads and reads
// (docs/adr/0002); inside the network its blob host resolves to this endpoint's address, through network.bicep's zone.

@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

@description('The storage account whose blobs the services reach privately.')
param storage_outputs_id string

@description('The network\'s subnet for private endpoints.')
param network_outputs_privateendpointssubnetid string

@description('The private DNS zone the blob host resolves in inside the network.')
param network_outputs_blobzoneid string

resource endpoint 'Microsoft.Network/privateEndpoints@2024-05-01' = {
  name: take('storage-pe-${uniqueString(resourceGroup().id)}', 64)
  location: location
  properties: {
    subnet: {
      id: network_outputs_privateendpointssubnetid
    }
    privateLinkServiceConnections: [
      {
        name: 'storage'
        properties: {
          privateLinkServiceId: storage_outputs_id
          groupIds: [
            'blob'
          ]
        }
      }
    ]
  }
}

resource zoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-05-01' = {
  parent: endpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'storage'
        properties: {
          privateDnsZoneId: network_outputs_blobzoneid
        }
      }
    ]
  }
}
