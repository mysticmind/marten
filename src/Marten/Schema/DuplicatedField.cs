using System;
using System.Linq.Expressions;
using System.Reflection;
using Baseline;
using Baseline.Reflection;
using Marten.Linq;
using Marten.Schema.Arguments;
using Marten.Storage;
using Marten.Util;
using NpgsqlTypes;

namespace Marten.Schema
{
    public class DuplicatedField: Field, IField
    {
        private readonly Func<Expression, object> _parseObject = expression => expression.Value();
        private readonly StoreOptions _storeOptions;
        private readonly bool useTimestampWithoutTimeZoneForDateTime;
        private string _columnName;

        [Obsolete("Please use constructor with StoreOptions parameter. This one will be removed in v4.0")]
        public DuplicatedField(EnumStorage enumStorage, MemberInfo[] memberPath, bool useTimestampWithoutTimeZoneForDateTime = true, bool notNull = false)
            : this(GetStoreOptions(enumStorage), memberPath, useTimestampWithoutTimeZoneForDateTime, notNull)
        {
        }

        public DuplicatedField(StoreOptions storeOptions, MemberInfo[] memberPath, bool useTimestampWithoutTimeZoneForDateTime = true, bool notNull = false)
            : base(storeOptions.DuplicatedFieldEnumStorage, memberPath, notNull)
        {
            ColumnName = MemberName.ToTableAlias();
            _storeOptions = storeOptions;
            this.useTimestampWithoutTimeZoneForDateTime = useTimestampWithoutTimeZoneForDateTime;

            if (MemberType.IsEnum)
            {
                if (storeOptions.DuplicatedFieldEnumStorage == EnumStorage.AsString)
                {
                    DbType = NpgsqlDbType.Varchar;
                    PgType = "varchar";

                    _parseObject = expression =>
                    {
                        var raw = expression.Value();
                        return Enum.GetName(MemberType, raw);
                    };
                }
                else
                {
                    DbType = NpgsqlDbType.Integer;
                    PgType = "integer";
                }
            }
            else if (MemberType.IsDateTime())
            {
                PgType = this.useTimestampWithoutTimeZoneForDateTime ? "timestamp without time zone" : "timestamp with time zone";
                DbType = this.useTimestampWithoutTimeZoneForDateTime ? NpgsqlDbType.Timestamp : NpgsqlDbType.TimestampTz;
            }
            else if (MemberType == typeof(DateTimeOffset) || MemberType == typeof(DateTimeOffset?))
            {
                PgType = "timestamp with time zone";
                DbType = NpgsqlDbType.TimestampTz;
            }
            else
            {
                DbType = TypeMappings.ToDbType(MemberType);
            }
        }

        /// <summary>
        /// Used to override the assigned DbType used by Npgsql when a parameter
        /// is used in a query against this column
        /// </summary>
        public NpgsqlDbType DbType { get; set; }

        public DuplicatedFieldRole Role { get; set; } = DuplicatedFieldRole.Search;

        public UpsertArgument UpsertArgument => new UpsertArgument
        {
            Arg = "arg_" + ColumnName.ToLower(),
            Column = ColumnName.ToLower(),
            PostgresType = PgType,
            Members = Members,
            DbType = DbType
        };

        public string SelectionLocator => SqlLocator;

        public string ColumnName
        {
            get { return _columnName; }
            set
            {
                _columnName = value;
                SqlLocator = "d." + _columnName;
            }
        }

        public void WritePatch(DocumentMapping mapping, SchemaPatch patch)
        {
            patch.Updates.Apply(mapping, $"ALTER TABLE {mapping.Table.QualifiedName} ADD COLUMN {ColumnName} {PgType.Trim()};");

            patch.UpWriter.WriteLine($"update {mapping.Table.QualifiedName} set {UpdateSqlFragment()};");
        }

        // TODO -- have this take in CommandBuilder
        public string UpdateSqlFragment()
        {
            if ((DbType & NpgsqlDbType.Array) == NpgsqlDbType.Array && PgType != "jsonb")
            {
                var jsonField = new JsonLocatorField("data", _storeOptions, _enumStorage, Casing.Default, Members, "jsonb");
                return $"{ColumnName} = CAST(ARRAY(SELECT jsonb_array_elements_text({jsonField.SqlLocator})) as {PgType})";
            }
            else
            {
                var jsonField = new JsonLocatorField("data", _storeOptions, _enumStorage, Casing.Default, Members, PgType);
                return $"{ColumnName} = {jsonField.SqlLocator}";
            }
        }

        public object GetValue(Expression valueExpression)
        {
            return _parseObject(valueExpression);
        }

        public bool ShouldUseContainmentOperator()
        {
            return false;
        }

        public string LocatorFor(string rootTableAlias)
        {
            return $"{rootTableAlias}.{_columnName}";
        }

        public string SqlLocator { get; set; }

        public static DuplicatedField For<T>(StoreOptions storeOptions, Expression<Func<T, object>> expression, bool useTimestampWithoutTimeZoneForDateTime = true, string pgType = null)
        {
            var accessor = ReflectionHelper.GetAccessor(expression);

            // Hokey, but it's just for testing for now.
            if (accessor is PropertyChain)
            {
                throw new NotSupportedException("Not yet supporting deep properties yet. Soon.");
            }

            var duplicate = new DuplicatedField(storeOptions, new MemberInfo[] { accessor.InnerProperty }, useTimestampWithoutTimeZoneForDateTime);
            if (pgType.IsNotEmpty())
            {
                duplicate.PgType = pgType;
            }
            return duplicate;
        }

        // I say you don't need a ForeignKey
        public virtual TableColumn ToColumn()
        {
            return new TableColumn(ColumnName, PgType);
        }

        [Obsolete("This method will be removed in v4.0 - it's only being kept for backward compatibility.")]
        private static StoreOptions GetStoreOptions(EnumStorage enumStorage)
        {
            var storeOptions = new StoreOptions();
            return storeOptions;
        }
    }
}
