using System.Collections.Generic;
using System.IO;
using System.Linq;
using Baseline;
using Marten.Schema;
using Marten.Storage;

namespace Marten.Transforms
{
    public class TransformSqlFunction: TransformFunction
    {
        public TransformSqlFunction(StoreOptions options, string name, string body)
            : base(options, name, body)
        {
            Name = name;
            Body = body;
        }

        protected override string toDropSql()
        {
            return ToDropSignature();
        }

        public override string GenerateFunction()
        {
            return Body;
        }
    }
}
