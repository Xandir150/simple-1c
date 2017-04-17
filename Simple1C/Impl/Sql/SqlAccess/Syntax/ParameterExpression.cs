﻿using Simple1C.Impl.Sql.Translation;

namespace Simple1C.Impl.Sql.SqlAccess.Syntax
{
    internal class ParameterExpression : ISqlElement
    {
        public string Name { get; set; }

        public ISqlElement Accept(SqlVisitor visitor)
        {
            return visitor.VisitParameter(this);
        }
    }
}