﻿using System.Collections.Generic;
using Simple1C.Impl.Sql.Translation;

namespace Simple1C.Impl.Sql.SqlAccess.Syntax
{
    internal class SelectClause : ISqlElement
    {
        public SelectClause()
        {
            JoinClauses = new List<JoinClause>();
            Fields = new List<SelectFieldExpression>();
        }

        public bool IsSelectAll { get; set; }
        public bool IsDistinct { get; set; }
        public int? Top { get; set; }
        public List<SelectFieldExpression> Fields { get; set; }
        public IColumnSource Source { get; set; }
        public ISqlElement WhereExpression { get; set; }
        public List<JoinClause> JoinClauses { get; private set; }
        public GroupByClause GroupBy { get; set; }
        public ISqlElement Having { get; set; }

        public ISqlElement Accept(SqlVisitor visitor)
        {
            return visitor.VisitSelect(this);
        }
    }
}