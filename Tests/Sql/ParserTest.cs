﻿using NUnit.Framework;
using Simple1C.Impl.Sql.SqlAccess.Parsing;
using Simple1C.Impl.Sql.SqlAccess.Syntax;

namespace Simple1C.Tests.Sql
{
    public class ParserTest
    {
        [Test]
        public void Simple()
        {
            var selectClause = Parse("select a,b from testTable");
            Assert.That(selectClause.Columns.Count, Is.EqualTo(2));
            Assert.That(selectClause.Columns[0].Alias, Is.Null);
            var aReference = (ColumnReferenceExpression) selectClause.Columns[0].Expression;
            Assert.That(aReference.Name, Is.EqualTo("a"));
            Assert.That(aReference.TableName, Is.EqualTo("testTable"));
            var bReference = (ColumnReferenceExpression) selectClause.Columns[1].Expression;
            Assert.That(bReference.Name, Is.EqualTo("b"));
            Assert.That(bReference.TableName, Is.EqualTo("testTable"));
            Assert.That(selectClause.Table.Name, Is.EqualTo("testTable"));
        }

        [Test]
        public void WhereCondition()
        {
            var selectClause = Parse("select a,b from testTable where c > 12");
            var binaryExpression = selectClause.WhereExpression as BinaryExpression;
            
            Assert.NotNull(binaryExpression);
            Assert.That(binaryExpression.Op, Is.EqualTo(SqlBinaryOperator.GreaterThan));
            
            var left = binaryExpression.Left as ColumnReferenceExpression;
            Assert.NotNull(left);
            Assert.That(left.Name, Is.EqualTo("c"));
            Assert.That(left.TableName, Is.EqualTo("testTable"));
            
            var right = binaryExpression.Right as LiteralExpression;
            Assert.NotNull(right);
            Assert.That(right.Value, Is.EqualTo(12));
        }
        
        [Test]
        public void InOperator()
        {
            var selectClause = Parse("select a,b from testTable where c in (10,20,30)");
            var inExpression = selectClause.WhereExpression as InExpression;
            
            Assert.NotNull(inExpression);
            Assert.That(inExpression.Column.Name, Is.EqualTo("c"));
            Assert.That(inExpression.Column.TableName, Is.EqualTo("testTable"));

            Assert.That(inExpression.Values.Count, Is.EqualTo(3));
            Assert.That(((LiteralExpression) inExpression.Values[0]).Value, Is.EqualTo(10));
            Assert.That(((LiteralExpression) inExpression.Values[1]).Value, Is.EqualTo(20));
            Assert.That(((LiteralExpression) inExpression.Values[2]).Value, Is.EqualTo(30));
        }
        
        [Test]
        public void LikeOperator()
        {
            var selectClause = Parse("select a,b from testTable where c like \"%test%\"");
            var binaryExpression = selectClause.WhereExpression as BinaryExpression;
            
            Assert.NotNull(binaryExpression);
            Assert.That(binaryExpression.Op, Is.EqualTo(SqlBinaryOperator.Like));

            var right = binaryExpression.Right as LiteralExpression;
            Assert.NotNull(right);
            Assert.That(right.Value, Is.EqualTo("%test%"));
        }
        
        [Test]
        public void StringLiteral()
        {
            var selectClause = Parse("select a,b from testTable where c != \"1\\\"2\\\"\"");
            var binaryExpression = selectClause.WhereExpression as BinaryExpression;
            
            Assert.NotNull(binaryExpression);
            Assert.That(binaryExpression.Op, Is.EqualTo(SqlBinaryOperator.Neq));

            var right = binaryExpression.Right as LiteralExpression;
            Assert.NotNull(right);
            Assert.That(right.Value, Is.EqualTo("1\"2\""));
        }
        
        [Test]
        public void Join()
        {
            var selectClause = Parse(@"select t1.a as nested1, t2.b as nested2
from testTable1 as t1
left join testTable2 as t2 on t1.id1 = t2.id2");
            Assert.That(selectClause.Columns.Count, Is.EqualTo(2));

            var col0 = selectClause.Columns[0];
            Assert.That(col0.Alias, Is.EqualTo("nested1"));
            var col0Reference = (ColumnReferenceExpression)col0.Expression;
            Assert.That(col0Reference.Name, Is.EqualTo("a"));
            Assert.That(col0Reference.TableName, Is.EqualTo("t1"));

            var col1 = selectClause.Columns[1];
            Assert.That(col1.Alias, Is.EqualTo("nested2"));
            var col1Reference = (ColumnReferenceExpression)col1.Expression;
            Assert.That(col1Reference.Name, Is.EqualTo("b"));
            Assert.That(col1Reference.TableName, Is.EqualTo("t2"));

            Assert.That(selectClause.Table.Name, Is.EqualTo("testTable1"));
            Assert.That(selectClause.Table.Alias, Is.EqualTo("t1"));

            Assert.That(selectClause.JoinClauses.Count, Is.EqualTo(1));
            Assert.That(selectClause.JoinClauses[0].JoinKind, Is.EqualTo(JoinKind.Left));
            Assert.That(selectClause.JoinClauses[0].Table.Name, Is.EqualTo("testTable2"));
            Assert.That(selectClause.JoinClauses[0].Table.Alias, Is.EqualTo("t2"));

            var binaryExpression = selectClause.JoinClauses[0].Condition as BinaryExpression;
            Assert.NotNull(binaryExpression);
            var left = binaryExpression.Left as ColumnReferenceExpression;
            Assert.NotNull(left);
            Assert.That(left.Name, Is.EqualTo("id1"));
            Assert.That(left.TableName, Is.EqualTo("t1"));

            var right = binaryExpression.Right as ColumnReferenceExpression;
            Assert.NotNull(right);
            Assert.That(right.Name, Is.EqualTo("id2"));
            Assert.That(right.TableName, Is.EqualTo("t2"));
        }

        [Test]
        public void ManyJoinClauses()
        {
            var selectClause = Parse(@"select *
from testTable1 as t1
left join testTable2 as t2 on t1.id1 = t2.id2
join testTable3 on t3.id3 = t1.id1
outer join testTable4 as t4 on t4.id4 = t1.id1");

            Assert.That(selectClause.JoinClauses.Count, Is.EqualTo(3));
            Assert.That(selectClause.JoinClauses[0].JoinKind, Is.EqualTo(JoinKind.Left));
            Assert.That(selectClause.JoinClauses[0].Table.Name, Is.EqualTo("testTable2"));
            Assert.That(selectClause.JoinClauses[0].Table.Alias, Is.EqualTo("t2"));
            
            Assert.That(selectClause.JoinClauses[1].JoinKind, Is.EqualTo(JoinKind.Inner));
            Assert.That(selectClause.JoinClauses[1].Table.Name, Is.EqualTo("testTable3"));
            Assert.That(selectClause.JoinClauses[1].Table.Alias, Is.Null);

            Assert.That(selectClause.JoinClauses[2].JoinKind, Is.EqualTo(JoinKind.Outer));
            Assert.That(selectClause.JoinClauses[2].Table.Name, Is.EqualTo("testTable4"));
            Assert.That(selectClause.JoinClauses[2].Table.Alias, Is.EqualTo("t4"));
        }
        
        [Test]
        public void FromAlias()
        {
            var selectClause = Parse("select a,b from testTable as tt");
            var aReference = (ColumnReferenceExpression) selectClause.Columns[0].Expression;
            Assert.That(aReference.Name, Is.EqualTo("a"));
            Assert.That(aReference.TableName, Is.EqualTo("tt"));
            var bReference = (ColumnReferenceExpression) selectClause.Columns[1].Expression;
            Assert.That(bReference.Name, Is.EqualTo("b"));
            Assert.That(bReference.TableName, Is.EqualTo("tt"));
            Assert.That(selectClause.Table.Name, Is.EqualTo("testTable"));
        }

        [Test]
        public void ColumnAliases()
        {
            var selectClause = Parse("select a as a_alias,b b_alias from testTable");
            Assert.That(selectClause.Columns[0].Alias, Is.EqualTo("a_alias"));
            Assert.That(selectClause.Columns[1].Alias, Is.EqualTo("b_alias"));
        }

        [Test]
        public void SelectAll()
        {
            var selectClause = Parse("select * from testTable");
            Assert.That(selectClause.IsSelectAll, Is.True);
            Assert.That(selectClause.Columns, Is.Null);
        }

        [Test]
        public void SelectAggregate()
        {
            var selectClause = Parse("select count(*) as a, Sum(*) AS b from testTable");
            var columnA = selectClause.Columns[0].Expression as AggregateFunction;
            var columnB = selectClause.Columns[1].Expression as AggregateFunction;
            Assert.NotNull(columnA);
            Assert.That(columnA.Type, Is.EqualTo(AggregateFunctionType.Count));
            Assert.NotNull(columnB);
            Assert.That(columnB.Type, Is.EqualTo(AggregateFunctionType.Sum));
        }

        private static SelectClause Parse(string source)
        {
            var parser = new QueryParser();
            return parser.Parse(source);
        }
    }
}