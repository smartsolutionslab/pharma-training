using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using JasperFx;
using Npgsql;
using Weasel.Core.Migrations;
using Wolverine;
using Wolverine.Runtime;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence;

/// <summary>
/// Wolverine's message storage in a module's database, built by the MigrationRunner as the administrator after the module's
/// migrations and before its role is ensured (docs/adr/0005): the administrator owns the schema <see cref="Schema"/> and
/// everything in it, and the role is granted what it needs there like in every other schema the administrator owns
/// (<see cref="ModuleDatabaseRole"/>). The role holds no CREATE on the database, so it could not build the storage, and owns
/// nothing it could attach code to (a trigger on the dead letters that runs as whoever replays them). It is built with the
/// configuration every service host uses (<c>PersistModuleMessages</c>), without a broker or anything else of the host's,
/// so what it builds is what a service's start-up check finds complete. A schema of that name some other role owns, or
/// that holds an object some other role owns, fails the run, naming the schema and the owner, like
/// <see cref="AdministratorSchema"/>: the storage is never built on, or granted from, what a role made. Nor does it hold
/// code Wolverine does not make: Wolverine makes tables, their indexes and sequences there, and nothing that runs on its
/// own, so a trigger (but the internal ones of a foreign key), a rule, a policy, row level security or a SECURITY DEFINER
/// function in the schema fails the run too, naming it, whoever owns it: code an operator attached by hand, or kept when
/// moving a schema a role made to the administrator, would run as whoever touches the storage. Cancellation reaches every
/// statement, the migration's included.
/// </summary>
public static class ModuleMessageStorage
{
    /// <summary>The schema Wolverine's storage lives in, in every module's database.</summary>
    public const string Schema = "wolverine";

    // Schema-qualified throughout, besides the session's pinned search_path (AdministratorSession).
    private const string CheckOwnership =
        $$"""
        DO $check$
        DECLARE
            owner_name name;
            foreign_object text;
            foreign_owner name;
            foreign_code text;
        BEGIN
            SELECT pg_catalog.pg_get_userbyid(nspowner) INTO owner_name FROM pg_catalog.pg_namespace WHERE nspname = '{{Schema}}';
            IF owner_name IS NULL THEN
                RETURN;
            END IF;

            IF owner_name <> CURRENT_USER THEN
                RAISE EXCEPTION USING
                    ERRCODE = 'insufficient_privilege',
                    MESSAGE = pg_catalog.format('The schema %I, Wolverine''s message storage, belongs to %I, not to the administrator %I.', '{{Schema}}', owner_name, CURRENT_USER),
                    HINT = 'Another role created it: inspect what it holds, then drop it, or remove what that role attached to it (triggers, rules, policies, functions), make it and what it holds the administrator''s (ALTER SCHEMA ... OWNER TO, REASSIGN OWNED), and run the migrations again (docs/adr/0005).';
            END IF;

            SELECT kind || ' ' || object_name, owner INTO foreign_object, foreign_owner FROM (
                SELECT 'relation' AS kind, c.relname::text AS object_name, pg_catalog.pg_get_userbyid(c.relowner) AS owner
                  FROM pg_catalog.pg_class AS c JOIN pg_catalog.pg_namespace AS n ON n.oid = c.relnamespace WHERE n.nspname = '{{Schema}}'
                UNION ALL
                SELECT 'function', p.oid::pg_catalog.regprocedure::text, pg_catalog.pg_get_userbyid(p.proowner)
                  FROM pg_catalog.pg_proc AS p JOIN pg_catalog.pg_namespace AS n ON n.oid = p.pronamespace WHERE n.nspname = '{{Schema}}'
                UNION ALL
                SELECT 'type', t.typname::text, pg_catalog.pg_get_userbyid(t.typowner)
                  FROM pg_catalog.pg_type AS t JOIN pg_catalog.pg_namespace AS n ON n.oid = t.typnamespace WHERE n.nspname = '{{Schema}}'
                UNION ALL
                SELECT 'operator', o.oprname::text, pg_catalog.pg_get_userbyid(o.oprowner)
                  FROM pg_catalog.pg_operator AS o JOIN pg_catalog.pg_namespace AS n ON n.oid = o.oprnamespace WHERE n.nspname = '{{Schema}}'
                UNION ALL
                SELECT 'collation', l.collname::text, pg_catalog.pg_get_userbyid(l.collowner)
                  FROM pg_catalog.pg_collation AS l JOIN pg_catalog.pg_namespace AS n ON n.oid = l.collnamespace WHERE n.nspname = '{{Schema}}'
                UNION ALL
                SELECT 'conversion', v.conname::text, pg_catalog.pg_get_userbyid(v.conowner)
                  FROM pg_catalog.pg_conversion AS v JOIN pg_catalog.pg_namespace AS n ON n.oid = v.connamespace WHERE n.nspname = '{{Schema}}'
                UNION ALL
                SELECT 'operator class', oc.opcname::text, pg_catalog.pg_get_userbyid(oc.opcowner)
                  FROM pg_catalog.pg_opclass AS oc JOIN pg_catalog.pg_namespace AS n ON n.oid = oc.opcnamespace WHERE n.nspname = '{{Schema}}'
                UNION ALL
                SELECT 'operator family', f.opfname::text, pg_catalog.pg_get_userbyid(f.opfowner)
                  FROM pg_catalog.pg_opfamily AS f JOIN pg_catalog.pg_namespace AS n ON n.oid = f.opfnamespace WHERE n.nspname = '{{Schema}}'
                UNION ALL
                SELECT 'text search dictionary', d.dictname::text, pg_catalog.pg_get_userbyid(d.dictowner)
                  FROM pg_catalog.pg_ts_dict AS d JOIN pg_catalog.pg_namespace AS n ON n.oid = d.dictnamespace WHERE n.nspname = '{{Schema}}'
                UNION ALL
                SELECT 'text search configuration', g.cfgname::text, pg_catalog.pg_get_userbyid(g.cfgowner)
                  FROM pg_catalog.pg_ts_config AS g JOIN pg_catalog.pg_namespace AS n ON n.oid = g.cfgnamespace WHERE n.nspname = '{{Schema}}'
                UNION ALL
                SELECT 'statistics object', x.stxname::text, pg_catalog.pg_get_userbyid(x.stxowner)
                  FROM pg_catalog.pg_statistic_ext AS x JOIN pg_catalog.pg_namespace AS n ON n.oid = x.stxnamespace WHERE n.nspname = '{{Schema}}'
            ) AS objects
             WHERE owner <> CURRENT_USER
             ORDER BY kind, object_name
             LIMIT 1;

            IF foreign_object IS NOT NULL THEN
                RAISE EXCEPTION USING
                    ERRCODE = 'insufficient_privilege',
                    MESSAGE = pg_catalog.format('The schema %I, Wolverine''s message storage, holds the %s, which belongs to %I, not to the administrator %I.', '{{Schema}}', foreign_object, foreign_owner, CURRENT_USER),
                    HINT = 'Another role created it: inspect it, drop it or make it the administrator''s, and run the migrations again.';
            END IF;

            -- Wolverine makes tables, their indexes and sequences, and nothing that runs by itself: whatever does fails the run.
            SELECT pg_catalog.string_agg(code, ', ' ORDER BY code) INTO foreign_code FROM (
                SELECT pg_catalog.format('trigger %I on %I.%I', t.tgname, n.nspname, c.relname) AS code
                  FROM pg_catalog.pg_trigger AS t
                  JOIN pg_catalog.pg_class AS c ON c.oid = t.tgrelid
                  JOIN pg_catalog.pg_namespace AS n ON n.oid = c.relnamespace
                 WHERE n.nspname = '{{Schema}}' AND NOT t.tgisinternal
                UNION ALL
                SELECT pg_catalog.format('rule %I on %I.%I', r.rulename, n.nspname, c.relname)
                  FROM pg_catalog.pg_rewrite AS r
                  JOIN pg_catalog.pg_class AS c ON c.oid = r.ev_class
                  JOIN pg_catalog.pg_namespace AS n ON n.oid = c.relnamespace
                 WHERE n.nspname = '{{Schema}}'
                UNION ALL
                SELECT pg_catalog.format('policy %I on %I.%I', p.polname, n.nspname, c.relname)
                  FROM pg_catalog.pg_policy AS p
                  JOIN pg_catalog.pg_class AS c ON c.oid = p.polrelid
                  JOIN pg_catalog.pg_namespace AS n ON n.oid = c.relnamespace
                 WHERE n.nspname = '{{Schema}}'
                UNION ALL
                SELECT pg_catalog.format('row level security on %I.%I', n.nspname, c.relname)
                  FROM pg_catalog.pg_class AS c
                  JOIN pg_catalog.pg_namespace AS n ON n.oid = c.relnamespace
                 WHERE n.nspname = '{{Schema}}' AND c.relrowsecurity
                UNION ALL
                SELECT pg_catalog.format('SECURITY DEFINER function %s', p.oid::pg_catalog.regprocedure)
                  FROM pg_catalog.pg_proc AS p
                  JOIN pg_catalog.pg_namespace AS n ON n.oid = p.pronamespace
                 WHERE n.nspname = '{{Schema}}' AND p.prosecdef
            ) AS attached;

            IF foreign_code IS NOT NULL THEN
                RAISE EXCEPTION USING
                    ERRCODE = 'insufficient_privilege',
                    MESSAGE = pg_catalog.format('The schema %I, Wolverine''s message storage, holds what Wolverine does not make and would run as whoever touches it: %s.', '{{Schema}}', foreign_code),
                    HINT = 'Someone attached it by hand, or a role made it before the schema was the administrator''s: inspect it, drop it, and run the migrations again.';
            END IF;
        END
        $check$
        """;

    /// <summary>
    /// Builds, or brings up to date, Wolverine's storage in the database named by <paramref name="administrator"/>, logged in
    /// as the administrator; nothing else of Wolverine runs, and no broker is needed.
    /// </summary>
    public static async Task BuildAsync(string administrator, CancellationToken cancellationToken)
    {
        Ensure.That(administrator, nameof(administrator)).IsNotNullOrWhiteSpace();

        var pinned = AdministratorSession.Pinned(administrator);
        await CheckOwnershipAsync(pinned, cancellationToken);

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddWolverine(options =>
        {
            options.Discovery.DisableConventionalDiscovery();
            options.PersistModuleMessages(pinned);
        });
        using var host = builder.Build();

        // The host is never started: its storage is migrated, and that is all. Wolverine's own MigrateAsync takes no
        // cancellation token, so the migration runs through the store's Weasel database, which does, and is what a service's
        // start-up check compares the storage with.
        if (host.Services.GetRequiredService<IWolverineRuntime>().Storage is not IDatabase storage)
        {
            throw new InvalidOperationException("Wolverine's message store is no Weasel database; the runner cannot migrate it.");
        }

        try
        {
            await storage.ApplyAllConfiguredChangesToDatabaseAsync(AutoCreate.CreateOrUpdate, ct: cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            // Wolverine reports a statement the cancellation ended as a failed migration (WolverineSchemaException); it was
            // cancelled, and says so.
            throw new OperationCanceledException("Building Wolverine's message storage was cancelled while it migrated.", exception, cancellationToken);
        }

        await CheckOwnershipAsync(pinned, cancellationToken);
    }

    private static async Task CheckOwnershipAsync(string administrator, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(administrator);
        await connection.OpenAsync(cancellationToken);
        await using var check = new NpgsqlCommand(CheckOwnership, connection);
        await check.ExecuteNonQueryAsync(cancellationToken);
    }
}
