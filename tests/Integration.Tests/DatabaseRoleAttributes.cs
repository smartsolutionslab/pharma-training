namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>What <c>pg_roles</c> says a role may do, as <see cref="ModuleDatabaseRoleTests"/> reads it.</summary>
public sealed record DatabaseRoleAttributes(bool CanLogin, bool Superuser, bool CreateDatabase, bool CreateRole, bool Replication, bool BypassRowLevelSecurity, bool Inherit);
