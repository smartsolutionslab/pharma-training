# 0038: The database and the vault are reached through the network alone

**Status:** Accepted (#149; the owner chose OQ8 (d) on #114, 2026-11-22, and option (a) for Blob Storage on #149, 2026-11-25).

## Context

- As Aspire 13.6.5 publishes the AppHost (ADR 0005), the PostgreSQL Flexible Server, the Key Vault and the storage account each have a public endpoint.
- The server also carries Aspire's firewall rule `AllowAllAzureIps` (`0.0.0.0`–`0.0.0.0`), which admits every Azure address, any tenant's included. Its password is all that stands in the way.
- #114 isolated the modules from each other: encrypted traffic between the apps, a Key Vault reference per secret an app reads, a database role per module. The network was left open. The owner decided it must be closed before real customer data.
- **What Aspire 13.6.5 offers.** Aspire.Hosting.Azure.Network models virtual networks, subnets, delegated subnets and private endpoints (`AddAzureVirtualNetwork`, `AddSubnet`, `WithDelegatedSubnet`, `AddPrivateEndpoint`). A resource with a private endpoint loses its public access, and the server its `AllowAllAzureIps` rule.
  - Every one of those APIs is marked for evaluation only (`ASPIREAZURE003`), and compiles only with that diagnostic silenced.
  - Azure.Provisioning's private DNS types are marked the same way (`AZPROVISION001`).
  - AGENTS.md forbids silencing a diagnostic.

## Decision

- **The network is a Bicep template of the AppHost's own,** `src/AppHost/network/network.bicep`, added by `AddBicepTemplate` as the module `network` (`src/AppHost/Wiring/PrivateNetwork.cs`), publish mode only. It holds:
  - a virtual network, `10.0.0.0/16`;
  - the subnet `aca-subnet` (`10.0.0.0/23`), delegated to `Microsoft.App/environments`;
  - the subnet `private-endpoints` (`10.0.2.0/24`), with a private endpoint for the server (`postgresqlServer`) and one for the vault (`vault`);
  - the private DNS zones `privatelink.postgres.database.azure.com` and `privatelink.vaultcore.azure.net`, linked to the network, each endpoint registering its address in its zone.
  - Aspire passes it the server's and the vault's ids and wires it into `main.bicep` like any module.
- **The services reach Blob Storage through a private endpoint (`blob`), and the account keeps its public endpoint** (the owner's option (a) on #149).
  - Browsers upload to the account and read from it directly, with the URLs the services sign (ADR 0002), so its public endpoint stays.
  - What guards it there:
    - every URL is a SAS on one blob, `Create` or `Read` alone, for at most 15 minutes;
    - it is signed with a user delegation key under the service's own identity, which may use its own container alone;
    - shared-key access is off, so no account key exists to sign with;
    - copies are taken from the deployment's own Entra tenant alone (`allowedCopyScope`, #159).
  - Inside the network, the blob host resolves to the endpoint's address through the zone `privatelink.blob.<storage suffix>`, which `network.bicep` declares and links. The services' own reads, writes, copies and delegation keys therefore never leave it.
  - The endpoint itself is `src/AppHost/network/blob-endpoint.bicep`, a module of its own. The account comes after the environment (its CORS rule names the environment's domain), and the environment after the network, so the network cannot wait for the account.
- **The Container Apps environment is injected into `aca-subnet`.** It takes the subnet's id from the network's output (`InjectInto`, in the environment's `ConfigureInfrastructure`). Its ingress stays external, so `host` and `auth` answer the internet as before. Every app now reaches the server's and the vault's names at their private addresses.
- **The server has no public endpoint:** `publicNetworkAccess: 'Disabled'`, and every firewall rule removed, `AllowAllAzureIps` included (`DenyPublicAccess`, in the server's `ConfigureInfrastructure`).
- **The vault has no public endpoint:** `publicNetworkAccess: 'Disabled'`, and `networkAcls` deny by default with bypass `None`. No trusted-service bypass would help:
  - Container Apps is not one of Key Vault's trusted services, so a Key Vault reference resolves through the network and the private zone alone;
  - the deployment's writes of secrets (`Microsoft.KeyVault/vaults/secrets` in Bicep) go through Azure Resource Manager's management plane, which the vault's firewall does not filter.
- **Operators reach the database from inside the environment,** through a temporary Container App in it that runs `psql` (README, "Operators inside the network"). Keycloak's administration (`az containerapp exec … keycloak`) is unchanged: `exec` goes through Azure's control plane, not the network.
- Once Aspire's networking API is no longer for evaluation, the template can give way to it. The publish-mode tests (`AppHostPrivateNetworkTests`) state the outcome, not the means.

## Consequences

- No public address reaches the server or the vault. A database password or a vault role that leaks is of no use from outside the network (`AppHostPrivateNetworkTests`, against the generated Bicep, in staging and production alike).
- **Blob Storage keeps its public endpoint, behind the SAS controls above.** Whoever holds no signed URL reaches nothing in it: Entra sign-in or a URL a service signed is the only way in. Serving every file through the services was the alternative; the owner chose against it (#149).
- The registry and the environment's own storage account (the broker's Azure Files volume) keep their public endpoints too. Both answer Entra identities alone. The owner moved them out of #149; #412 reviews them after the first deploy.
- **An environment's network is fixed when it is created.** Container Apps cannot move an existing environment into a subnet. An environment deployed before this change must be deleted and deployed again, data restored into a new server. None exists yet (#129).
- What only the first deploy shows (#129), each with its fix in the AppHost:
  - that Container Apps resolve the vault's Key Vault references through the private endpoint. A reference that does not resolve points at the zone `privatelink.vaultcore.azure.net`, its link to the network, or the endpoint's DNS record, never at the bypass, which stays `None` (#415);
  - that the deployment's own writes of secrets into the vault pass, as management-plane writes should;
  - that the init containers' migrations reach the server by its private address on their first run;
  - that the services' blob calls resolve to the blob endpoint, while a browser's upload and read still reach the public endpoint.
- Reading a vault secret by hand (`az keyvault secret show`) from outside the network no longer works. A secret an operator needs is read from inside the environment, as a Key Vault reference of the temporary app.
- The templates look their subnets up by name, and the blobs' zone falls back to public DNS for a name it holds no record of (`resolutionPolicy: 'NxDomainRedirect'`). Another storage account's blob host, which Azure also resolves through `privatelink.blob`, still resolves from inside the network (#415).
- CI's publish jobs compile every published Bicep file, these templates included (`az bicep build`).
