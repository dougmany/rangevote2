using Dapper;
using System;
using System.Data;

namespace RangeVote2.Data
{
    public class GuidTypeHandler : SqlMapper.TypeHandler<Guid>
    {
        public override Guid Parse(object value)
        {
            if (value == null || value is DBNull)
                return Guid.Empty;

            if (value is string stringValue)
            {
                return Guid.Parse(stringValue);
            }

            if (value is Guid guidValue)
            {
                return guidValue;
            }

            throw new InvalidCastException($"Unable to convert {value.GetType()} to Guid");
        }

        public override void SetValue(IDbDataParameter parameter, Guid value)
        {
            parameter.Value = value.ToString();
        }
    }

    public class NullableGuidTypeHandler : SqlMapper.TypeHandler<Guid?>
    {
        public override Guid? Parse(object value)
        {
            if (value == null || value is DBNull)
                return null;

            if (value is string stringValue)
            {
                if (string.IsNullOrEmpty(stringValue))
                    return null;
                return Guid.Parse(stringValue);
            }

            if (value is Guid guidValue)
            {
                return guidValue;
            }

            throw new InvalidCastException($"Unable to convert {value.GetType()} to Guid?");
        }

        public override void SetValue(IDbDataParameter parameter, Guid? value)
        {
            parameter.Value = value?.ToString() ?? (object)DBNull.Value;
        }
    }
}
