using System.Reflection;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using NetShield.Contracts.Messaging;

namespace NetShield.ArchitectureTests.Solution;

/// <summary>
/// The two ARCHITECTURE.md §4 rules that are about types rather than about project references:
/// cross-module communication carries <c>Contracts</c> types only, and no module exposes an EF
/// entity across its boundary.
/// </summary>
/// <remarks>
/// <para>
/// These are checked by reflection, which <c>ModuleReferenceTests</c> could not be: a rule about
/// what a type looks like is not observable in a <c>.csproj</c>. They were left unwritten until
/// WP-1.1 on purpose — until <c>Device</c> existed, every module assembly was empty and a
/// reflection rule would have passed over all ten of them vacuously, reporting a confidence it
/// did not have.
/// </para>
/// <para>
/// Written by hand rather than with NetArchTest or ArchUnitNET. Two rules expressed in a dozen
/// lines of reflection are easier to read — and to be sure of — than the same two expressed
/// through a fluent library the reader also has to know. If the rule set grows to where that
/// stops being true, a library is the answer then.
/// </para>
/// </remarks>
public sealed class ModuleBoundaryTests
{
    /// <summary>
    /// An event travels between modules, so its type has to be one every module can name.
    /// <c>Contracts</c> is the only assembly all of them reference.
    /// </summary>
    [Fact]
    public void EveryIntegrationEvent_IsDeclaredInContracts()
    {
        IReadOnlyList<string> offenders =
        [
            .. ModuleAssemblies
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type => typeof(IIntegrationEvent).IsAssignableFrom(type))
                .Where(type => type is { IsInterface: false, IsAbstract: false })
                .Select(type => $"{type.FullName} in {type.Assembly.GetName().Name}")
        ];

        offenders.Should().BeEmpty(
            "ARCHITECTURE.md §4: cross-module communication carries Contracts types only, and an "
            + "event declared inside a module is a type no other module may reference");
    }

    /// <summary>
    /// An entity may not appear in a signature anything outside its module can call. The module's
    /// own <c>DbContext</c> is exempt: a context handing back its own entities is the module
    /// talking to itself, not across a boundary.
    /// </summary>
    [Fact]
    public void NoModule_ExposesAnEntity_OnItsPublicSurface()
    {
        IReadOnlyList<string> offenders =
        [
            .. from assembly in ModuleAssemblies
               let entities = EntityTypesOf(assembly)
               where entities.Count > 0
               from type in assembly.GetTypes()
               where type.IsPublic && !IsDbContext(type) && !entities.Contains(type)
               from member in PublicSignatureTypes(type)
               where entities.Contains(member.Type)
               select $"{type.FullName}.{member.Member} exposes {member.Type.Name}"
        ];

        offenders.Should().BeEmpty(
            "ARCHITECTURE.md §4: DTOs cross a module boundary and entities do not");
    }

    /// <summary>
    /// The same rule from the other side: <c>Contracts</c> is what crosses, so nothing an entity
    /// is may be declared there.
    /// </summary>
    [Fact]
    public void NoEntity_IsDeclaredInContracts()
    {
        Assembly contracts = typeof(IIntegrationEvent).Assembly;

        IReadOnlyList<string> offenders =
        [
            .. ModuleAssemblies
                .SelectMany(EntityTypesOf)
                .Where(entity => entity.Assembly == contracts)
                .Select(entity => entity.FullName ?? entity.Name)
        ];

        offenders.Should().BeEmpty("an entity in Contracts would be an entity every module shares");
    }

    /// <summary>
    /// Every module assembly that is loaded. A module with no types yet contributes nothing and
    /// is not a pass — the rules above only report on assemblies that actually hold something.
    /// </summary>
    private static IReadOnlyList<Assembly> ModuleAssemblies { get; } =
    [
        typeof(Inventory.Persistence.InventoryDbContext).Assembly,
        typeof(Identity.Persistence.IdentityDbContext).Assembly
    ];

    /// <summary>
    /// The CLR types a module's contexts map. Read from the model rather than guessed at by name
    /// or by folder, so a type is an entity here for exactly the reason it is one at runtime.
    /// </summary>
    private static IReadOnlyCollection<Type> EntityTypesOf(Assembly assembly)
    {
        HashSet<Type> entities = [];

        foreach (Type contextType in assembly.GetTypes().Where(IsDbContext))
        {
            // The model is built from the mapping alone; nothing here opens a connection.
            using DbContext context = (DbContext)Activator.CreateInstance(
                contextType,
                OptionsFor(contextType))!;

            foreach (IEntityType entity in context.Model.GetEntityTypes())
            {
                entities.Add(entity.ClrType);
            }
        }

        return entities;
    }

    private static object OptionsFor(Type contextType)
    {
        Type builderType = typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType);
        object builder = Activator.CreateInstance(builderType)!;

        // A connection string is never used: GetEntityTypes reads the model, not the database.
        NpgsqlDbContextOptionsBuilderExtensions.UseNpgsql(
            (DbContextOptionsBuilder)builder,
            "Host=architecture-test");

        // DeclaredOnly: the generic builder and its base both declare Options, and asking for
        // the name alone is ambiguous.
        return builderType.GetProperty(
                nameof(DbContextOptionsBuilder<DbContext>.Options),
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!
            .GetValue(builder)!;
    }

    private static bool IsDbContext(Type type) =>
        typeof(DbContext).IsAssignableFrom(type) && type is { IsAbstract: false, IsGenericTypeDefinition: false };

    /// <summary>
    /// Every type a caller outside the assembly could see in this type's signature: what its
    /// public methods take and return, and what its public properties and fields are.
    /// </summary>
    private static IEnumerable<(string Member, Type Type)> PublicSignatureTypes(Type type)
    {
        const BindingFlags Visible =
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (PropertyInfo property in type.GetProperties(Visible))
        {
            yield return (property.Name, Unwrap(property.PropertyType));
        }

        foreach (FieldInfo field in type.GetFields(Visible))
        {
            yield return (field.Name, Unwrap(field.FieldType));
        }

        foreach (MethodInfo method in type.GetMethods(Visible).Where(candidate => !candidate.IsSpecialName))
        {
            yield return (method.Name, Unwrap(method.ReturnType));

            foreach (ParameterInfo parameter in method.GetParameters())
            {
                yield return ($"{method.Name}({parameter.Name})", Unwrap(parameter.ParameterType));
            }
        }
    }

    /// <summary>
    /// Reaches through the wrappers an entity would otherwise hide behind — <c>DbSet&lt;T&gt;</c>,
    /// <c>Task&lt;T&gt;</c>, <c>IReadOnlyList&lt;T&gt;</c>, an array. Returning
    /// <c>IReadOnlyList&lt;Device&gt;</c> exposes <c>Device</c> just as surely as returning one.
    /// </summary>
    private static Type Unwrap(Type type)
    {
        if (type.IsArray)
        {
            return Unwrap(type.GetElementType()!);
        }

        return type.IsGenericType && type.GetGenericArguments() is [Type single]
            ? Unwrap(single)
            : type;
    }
}
