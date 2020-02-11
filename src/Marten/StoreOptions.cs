using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Baseline;
using Marten.Events;
using Marten.Schema;
using Marten.Schema.Identity;
using Marten.Schema.Identity.Sequences;
using Marten.Services;
using Marten.Storage;
using Marten.Transforms;
using Npgsql;

namespace Marten
{
    /// <summary>
    ///     StoreOptions supplies all the necessary configuration
    ///     necessary to customize and bootstrap a working
    ///     DocumentStore
    /// </summary>
    public class StoreOptions
    {
        /// <summary>
        ///     The default database schema used 'public'.
        /// </summary>
        public const string DefaultDatabaseSchemaName = "public";

        public const string PatchDoc = "patch_doc";

        private readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, ChildDocument>> _childDocs
            = new ConcurrentDictionary<Type, ConcurrentDictionary<string, ChildDocument>>();

        public StorageFeatures Storage { get; }
        public readonly IList<IInitialData> InitialData = new List<IInitialData>();

        /// <summary>
        ///     Add, remove, or reorder global session listeners
        /// </summary>
        public readonly IList<IDocumentSessionListener> Listeners = new List<IDocumentSessionListener>();

        /// <summary>
        ///     Modify the document and event store database mappings for indexes and searching options
        /// </summary>
        public readonly MartenRegistry Schema;

        private string _databaseSchemaName = DefaultDatabaseSchemaName;

        private IMartenLogger _logger = new NulloMartenLogger();
        private ISerializer _serializer;
        private EnumStorage? _duplicatedFieldEnumStorage;

        private IRetryPolicy _retryPolicy = new NulloRetryPolicy();

        /// <summary>
        ///     Whether or Marten should attempt to create any missing database schema objects at runtime. This
        ///     property is "All" by default for more efficient development, but can be set to lower values for production usage.
        /// </summary>
        public AutoCreate AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;

        /// <summary>
        /// Configure Marten to create databases for tenants in case databases do not exist or need to be dropped & re-created
        /// </summary>
        /// <remarks>Creating and dropping databases requires the CREATEDB privilege</remarks>
        public void CreateDatabasesForTenants(Action<IDatabaseCreationExpressions> configure)
        {
            CreateDatabases = configure ?? throw new ArgumentNullException(nameof(configure));
        }

        public Action<IDatabaseCreationExpressions> CreateDatabases { get; set; }

        public StoreOptions()
        {
            Events = new EventGraph(this);
            Schema = new MartenRegistry();
            Transforms = new Transforms.Transforms(this);
            Storage = new StorageFeatures(this);
        }

        /// <summary>
        ///     Sets the database default schema name used to store the documents.
        /// </summary>
        public string DatabaseSchemaName
        {
            get { return _databaseSchemaName; }
            set { _databaseSchemaName = value?.ToLowerInvariant(); }
        }

        /// <summary>
        ///     Global default parameters for Hilo sequences within the DocumentStore. Can be overridden per document
        ///     type as well
        /// </summary>
        public HiloSettings HiloSequenceDefaults { get; } = new HiloSettings();

        /// <summary>
        ///     Sets the batch size for updating or deleting documents in IDocumentSession.SaveChanges() /
        ///     IUnitOfWork.ApplyChanges()
        /// </summary>
        public int UpdateBatchSize { get; set; } = 500;

        /// <summary>
        ///     Configures the store to use char buffer pooling, greatly reducing allocations for serializing documents and events.
        ///     The default is true.
        /// </summary>
        public bool UseCharBufferPooling { get; set; } = true;

        /// <summary>
        ///     Set the default Id strategy for the document mapping.
        /// </summary>
        public Func<IDocumentMapping, StoreOptions, IIdGeneration> DefaultIdStrategy { get; set; }

        /// <summary>
        ///     Configuration of event streams and projections
        /// </summary>
        public EventGraph Events { get; }

        /// <summary>
        ///     Extension point to add custom Linq query parsers
        /// </summary>
        public LinqCustomizations Linq { get; } = new LinqCustomizations();

        public ITransforms Transforms { get; }

        /// <summary>
        ///     Allows you to modify how the DDL for document tables and upsert functions is
        ///     written
        /// </summary>
        public DdlRules DdlRules { get; } = new DdlRules();

        /// <summary>
        ///     Used to validate database object name lengths against Postgresql's NAMEDATALEN property to avoid
        ///     Marten getting confused when comparing database schemas against the configuration. See
        ///     https://www.postgresql.org/docs/current/static/sql-syntax-lexical.html
        ///     for more information. This does NOT adjust NAMEDATALEN for you.
        /// </summary>
        public int NameDataLength { get; set; } = 64;

        /// <summary>
        ///     Gets Enum values stored as either integers or strings
        /// </summary>
        public EnumStorage EnumStorage => Serializer().EnumStorage;

        /// <summary>
        ///     Sets Enum values stored as either integers or strings for DuplicatedField.
        ///     Please use only for migration from Marten 2.*. It might be removed in the next major version.
        /// </summary>
        [Obsolete("Please use only for migration from Marten 2.*. It might be removed in the next major version.")]
        public EnumStorage DuplicatedFieldEnumStorage
        {
            get { return _duplicatedFieldEnumStorage ?? EnumStorage; }
            set { _duplicatedFieldEnumStorage = value; }
        }

        /// <summary>
        ///     Decides if `timestamp without time zone` database type should be used for `DateTime` DuplicatedField.
        ///     Please use only for migration from Marten 2.*. It might be removed in the next major version.
        /// </summary>
        [Obsolete("Please use only for migration from Marten 2.*. It might be removed in the next major versions")]
        public bool DuplicatedFieldUseTimestampWithoutTimeZoneForDateTime { get; set; } = true;

        internal void CreatePatching()
        {
            /*var patching = PLV8Enabled
                ? new TransformFunction(this, PatchDoc, SchemaBuilder.GetJavascript(this, "mt_patching"), true, "plv8")
                : new TransformFunction(this, PatchDoc, SchemaBuilder.GetSqlScript(this.DatabaseSchemaName, "mt_patch_doc"), false, "plpgsql");*/

            var patching = new TransformSqlFunction(this, PatchDoc, SchemaBuilder.GetSqlScript(this.DatabaseSchemaName, "mt_patch_doc"));
            patching.OtherArgs.Add("patch");
            Transforms.Load(patching);
        }

        internal ChildDocument GetChildDocument(string locator, Type documentType)
        {
            var byType = _childDocs.GetOrAdd(documentType, type => new ConcurrentDictionary<string, ChildDocument>());

            return byType.GetOrAdd(locator, loc => new ChildDocument(locator, documentType, this));
        }

        /// <summary>
        ///     Supply the connection string to the Postgresql database
        /// </summary>
        /// <param name="connectionString"></param>
        public void Connection(string connectionString)
        {
            Tenancy = new DefaultTenancy(new ConnectionFactory(connectionString), this);
        }

        /// <summary>
        ///     Supply a source for the connection string to a Postgresql database
        /// </summary>
        /// <param name="connectionSource"></param>
        public void Connection(Func<string> connectionSource)
        {
            Tenancy = new DefaultTenancy(new ConnectionFactory(connectionSource), this);
        }

        /// <summary>
        ///     Supply a mechanism for resolving an NpgsqlConnection object to
        ///     the Postgresql database
        /// </summary>
        /// <param name="source"></param>
        public void Connection(Func<NpgsqlConnection> source)
        {
            Tenancy = new DefaultTenancy(new LambdaConnectionFactory(source), this);
        }

        /// <summary>
        ///     Override the JSON serialization by ISerializer type
        /// </summary>
        /// <param name="serializer"></param>
        public void Serializer(ISerializer serializer)
        {
            _serializer = serializer;
        }

        /// <summary>
        ///     Override the JSON serialization by an ISerializer of type "T"
        /// </summary>
        /// <typeparam name="T">The ISerializer type</typeparam>
        public void Serializer<T>() where T : ISerializer, new()
        {
            _serializer = new T();
        }

        public ISerializer Serializer()
        {
            return _serializer;
        }

        public IMartenLogger Logger()
        {
            return _logger ?? new NulloMartenLogger();
        }

        public void Logger(IMartenLogger logger)
        {
            _logger = logger;
        }

        public IRetryPolicy RetryPolicy()
        {
            return _retryPolicy ?? new NulloRetryPolicy();
        }

        public void RetryPolicy(IRetryPolicy retryPolicy)
        {
            _retryPolicy = retryPolicy;
        }

        /// <summary>
        ///     Force Marten to create document mappings for type T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public void RegisterDocumentType<T>()
        {
            RegisterDocumentType(typeof(T));
        }

        /// <summary>
        ///     Force Marten to create a document mapping for the document type
        /// </summary>
        /// <param name="documentType"></param>
        public void RegisterDocumentType(Type documentType)
        {
            if (Storage.MappingFor(documentType) == null)
                throw new Exception("Unable to create document mapping for " + documentType);
        }

        /// <summary>
        ///     Force Marten to create document mappings for all the given document types
        /// </summary>
        /// <param name="documentTypes"></param>
        public void RegisterDocumentTypes(IEnumerable<Type> documentTypes)
        {
            documentTypes.Each(RegisterDocumentType);
        }

        public void AssertValidIdentifier(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new PostgresqlIdentifierInvalidException(name);
            if (name.IndexOf(' ') >= 0)
                throw new PostgresqlIdentifierInvalidException(name);
            if (name.Length < NameDataLength)
                return;
            throw new PostgresqlIdentifierTooLongException(NameDataLength, name);
        }

        internal void ApplyConfiguration()
        {
            Schema.Apply(this);

            foreach (var mapping in Storage.AllDocumentMappings)
            {
                mapping.Validate();
            }
        }

        public ITenancy Tenancy { get; set; }

        private readonly IList<IDocumentPolicy> _policies = new List<IDocumentPolicy>();

        internal void applyPolicies(DocumentMapping mapping)
        {
            foreach (var policy in _policies)
            {
                policy.Apply(mapping);
            }
        }

        /// <summary>
        /// Validate that minimal options to initialize a document store have been specified
        /// </summary>
        internal void Validate()
        {
            if (Tenancy == null)
            {
                throw new InvalidOperationException("Tenancy not specified - provide either connection string or connection factory through Connection(..)");
            }
        }

        /// <summary>
        /// Apply conventional policies to how documents are mapped
        /// </summary>
        public PoliciesExpression Policies => new PoliciesExpression(this);

        public bool PLV8Enabled { get; set; } = true;

        public class PoliciesExpression
        {
            private readonly StoreOptions _parent;

            public PoliciesExpression(StoreOptions parent)
            {
                _parent = parent;
            }

            public PoliciesExpression OnDocuments<T>() where T : IDocumentPolicy, new()
            {
                return OnDocuments(new T());
            }

            public PoliciesExpression OnDocuments(IDocumentPolicy policy)
            {
                _parent._policies.Add(policy);
                return this;
            }

            public PoliciesExpression ForAllDocuments(Action<DocumentMapping> configure)
            {
                return OnDocuments(new LambdaDocumentPolicy(configure));
            }

            public PoliciesExpression AllDocumentsAreMultiTenanted()
            {
                return ForAllDocuments(_ => _.TenancyStyle = TenancyStyle.Conjoined);
            }
        }
    }

    public interface IDocumentPolicy
    {
        void Apply(DocumentMapping mapping);
    }

    internal class LambdaDocumentPolicy: IDocumentPolicy
    {
        private readonly Action<DocumentMapping> _modify;

        public LambdaDocumentPolicy(Action<DocumentMapping> modify)
        {
            _modify = modify;
        }

        public void Apply(DocumentMapping mapping)
        {
            _modify(mapping);
        }
    }
}
