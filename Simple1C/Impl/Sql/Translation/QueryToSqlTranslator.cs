﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Simple1C.Impl.Helpers;
using Simple1C.Impl.Sql.SchemaMapping;
using Simple1C.Impl.Sql.SqlAccess;
using Simple1C.Impl.Sql.SqlAccess.Parsing;
using Simple1C.Impl.Sql.SqlAccess.Syntax;
using Simple1C.Interface;

namespace Simple1C.Impl.Sql.Translation
{
    internal class QueryToSqlTranslator
    {
        private static readonly Regex nowMacroRegex = new Regex(@"&Now",
            RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

        private static readonly Regex dateTimeRegex = new Regex(@"(?<year>\d+)[\,\s]+(?<month>\d+)[\,\s]+(?<day>\d+)",
            RegexOptions.Compiled | RegexOptions.Singleline);

        //private static readonly Regex joinRegex = new Regex(@"join\s+\S+\s+as\s+(\S+)\s+on\s+",
        //    RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

        private static readonly Dictionary<string, string> keywordsMap =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                {"выбрать", "select"},
                {"представление", "presentation"},
                {"как", "as"},
                {"из", "from"},
                {"где", "where"},
                {"и", "and"},
                {"или", "or"}
            };

        private static readonly Regex keywordsRegex = new Regex(string.Format(@"\b({0})\b",
            keywordsMap.Keys.JoinStrings("|")),
            RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

        private static readonly Dictionary<string, Func<QueryToSqlTranslator, string, string>> functions =
            new Dictionary<string, Func<QueryToSqlTranslator, string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                {"датавремя", (_, s) => FormatDateTime(s)},
                {"год", (_, s) => string.Format("date_part('year', {0})", s)},
                {"квартал", (_, s) => string.Format("date_trunc('quarter', {0})", s)},
                {"значение", (t, s) => t.GetEnumValueSql(s)}
            };

        private static readonly Dictionary<string, Regex> functionRegexes = functions.Keys
            .ToDictionary(x => x, x => new Regex(string.Format(@"{0}\(([^\)]+)\)", x),
                RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase));

        private static readonly Regex propertiesRegex = new Regex(GetPropertiesRegex(),
            RegexOptions.Compiled | RegexOptions.Singleline);

        private static string GetPropertiesRegex()
        {
            const string propRegex = @"[a-zA-Z]+\.[а-яА-Яa-zA-Z0-9\.]+";
            return string.Format(@"(?<func>ПРЕДСТАВЛЕНИЕ)\((?<prop>{0})\)|(?<prop>{0})",
                propRegex);
        }

        private Dictionary<string, MainQueryEntity> queryTables = new Dictionary<string, MainQueryEntity>(StringComparer.OrdinalIgnoreCase);

        private const byte configurationItemReferenceType = 8;

        private readonly NameGenerator nameGenerator = new NameGenerator();
        private readonly IMappingSource mappingSource;
        private readonly List<ISqlElement> areas;

        public QueryToSqlTranslator(IMappingSource mappingSource, int[] areas)
        {
            this.mappingSource = mappingSource;
            if (areas.Length > 0)
                this.areas = areas.Select(x => new LiteralExpression {Value = x})
                    .Cast<ISqlElement>()
                    .ToList();
        }

        public DateTime? CurrentDate { get; set; }

        public string Translate(string source)
        {
            var currentDateString = FormatSqlDate(CurrentDate ?? DateTime.Today);
            source = keywordsRegex.Replace(source, m => keywordsMap[m.Groups[1].Value]);
            source = nowMacroRegex.Replace(source, currentDateString);
            var queryParser = new QueryParser();
            var rootSelectClause = queryParser.Parse(source);
            var selectClause = rootSelectClause;
            while (selectClause != null)
            {
                var union = selectClause.Union;
                selectClause.Union = null;
                TranslateSingleSelect(selectClause);
                selectClause.Union = union;
                selectClause = union == null ? null : union.SelectClause;
            }
            return SqlFormatter.Format(rootSelectClause);
        }

        private void RegisterMainQueryEntity(string name, string queryName)
        {
            var queryEntity = CreateQueryEntity(null, queryName);
            var mainQueryEntity = new MainQueryEntity(queryEntity, areas != null);
            queryTables.Add(name, mainQueryEntity);
        }

        private void TranslateSingleSelect(SelectClause selectClause)
        {
            nameGenerator.Reset();
            queryTables.Clear();

            var referencePatcher = new ColumnReferencePatcher(this);
            referencePatcher.Visit(selectClause);

            //todo
            //queryText = joinRegex.Replace(queryText, m => PatchJoin(m.Value, m.Index, m.Groups[1].Value));

            var tableDeclarationPatcher = new TableDeclarationPatcher(this);
            tableDeclarationPatcher.Visit(selectClause);

            //queryText = functions.Aggregate(queryText, (s, f) => functionRegexes[f.Key]
            //    .Replace(s, m => f.Value(this, m.Groups[1].Value)));
            //return queryText;
        }

        private string GetEnumValueSql(string enumValue)
        {
            var enumValueItems = enumValue.Split('.');
            var table = CreateQueryEntity(null, enumValueItems[0] + "." + enumValueItems[1]);
            var selectClause = new SelectClause {Source = GetDeclarationClause(table)};
            selectClause.Fields.Add(new SelectField
            {
                Expression = new ColumnReferenceExpression
                {
                    Name = table.GetSingleColumnName("Ссылка"),
                    TableName = GetQueryEntityAlias(table)
                }
            });
            var enumMappingsJoinClause = CreateEnumMappingsJoinClause(table);
            selectClause.JoinClauses.Add(enumMappingsJoinClause);
            selectClause.WhereExpression = new EqualityExpression
            {
                Left = new ColumnReferenceExpression
                {
                    Name = "enumValueName",
                    TableName = enumMappingsJoinClause.Source.Alias
                },
                Right = new LiteralExpression
                {
                    Value = enumValueItems[2]
                }
            };
            return SqlFormatter.Format(selectClause);
        }

        private MainQueryEntity GetMainQueryEntity(string alias)
        {
            MainQueryEntity mainEntity;
            if (!queryTables.TryGetValue(alias, out mainEntity))
            {
                const string messageFormat = "can't find query table by alias [{0}]";
                throw new InvalidOperationException(string.Format(messageFormat, alias));
            }
            return mainEntity;
        }

        //private class AreaConditionJoinClausePatcher : SqlVisitor
        //{
        //    public override ISqlElement VisitJoin(JoinClause clause)
        //    {
        //        return base.VisitJoin(clause);
        //    }
        //}

        //private string PatchJoin(string joinText, int joinPosition, string alias)
        //{
        //    var fromPosition = queryText.LastIndexOf("from", joinPosition, StringComparison.OrdinalIgnoreCase);
        //    if (fromPosition < 0)
        //        throw new InvalidOperationException("assertion failure");
        //    var tableMatch = tableNameRegex.Match(queryText, fromPosition);
        //    if (!tableMatch.Success)
        //        throw new InvalidOperationException("assertion failure");
        //    var mainTableAlias = tableMatch.Groups[3].Value;
        //    GetOrCreateQueryField(new[] {mainTableAlias, "ОбластьДанныхОсновныеДанные"}, false, SelectPart.Join);
        //    GetOrCreateQueryField(new[] {alias, "ОбластьДанныхОсновныеДанные"}, false, SelectPart.Join);
        //    var condition = string.Format("{0}.ОбластьДанныхОсновныеДанные = {1}.ОбластьДанныхОсновныеДанные and ",
        //        mainTableAlias, alias);
        //    return joinText + condition;
        //}

        private class QueryField
        {
            public readonly string alias;
            public readonly QueryEntityProperty[] properties;
            public readonly bool invert;

            public QueryField(string alias, QueryEntityProperty[] properties, bool invert)
            {
                this.alias = alias;
                this.properties = properties;
                this.invert = invert;
            }

            public readonly List<SelectPart> parts = new List<SelectPart>();
        }

        private QueryField GetOrCreateQueryField(string[] propertyNames, bool isRepresentation, SelectPart selectPart)
        {
            var mainEntity = GetMainQueryEntity(propertyNames[0]);
            var keyWithoutFunction = string.Join(".", propertyNames);
            if (!isRepresentation && selectPart == SelectPart.GroupBy)
            {
                QueryField fieldWithFunction;
                var keyWithFunction = keyWithoutFunction + "." + true;
                if (mainEntity.fields.TryGetValue(keyWithFunction, out fieldWithFunction))
                    if (fieldWithFunction.parts.Contains(SelectPart.Select))
                        isRepresentation = true;
            }
            var key = keyWithoutFunction + "." + isRepresentation;
            QueryField field;
            if (!mainEntity.fields.TryGetValue(key, out field))
            {
                var subqueryRequired = propertyNames.Length > 2;
                bool needInvert = false;
                if (propertyNames[propertyNames.Length - 1] == "ЭтоГруппа")
                {
                    needInvert = true;
                    subqueryRequired = true;
                }
                var referencedProperties = new List<QueryEntityProperty>();
                EnumProperties(propertyNames, mainEntity.queryEntity, 1, referencedProperties);
                if (referencedProperties.Count == 0)
                    throw new InvalidOperationException("assertion failure");
                if (isRepresentation)
                    if (ReplaceWithRepresentation(referencedProperties))
                        subqueryRequired = true;
                string fieldAlias = null;
                if (subqueryRequired)
                {
                    mainEntity.subqueryRequired = true;
                    fieldAlias = nameGenerator.GenerateColumnName();
                }
                foreach (var p in referencedProperties)
                    p.referenced = true;
                field = new QueryField(fieldAlias, referencedProperties.ToArray(), needInvert);
                mainEntity.fields.Add(key, field);
            }
            if (!field.parts.Contains(selectPart))
                field.parts.Add(selectPart);
            return field;
        }

        private bool ReplaceWithRepresentation(List<QueryEntityProperty> properties)
        {
            var result = false;
            for (var i = properties.Count - 1; i >= 0; i--)
            {
                var property = properties[i];
                if (property.nestedEntities.Count == 0)
                    continue;
                properties.RemoveAt(i);
                foreach (var nestedEntity in property.nestedEntities)
                {
                    var scope = nestedEntity.mapping.ObjectName.HasValue
                        ? nestedEntity.mapping.ObjectName.Value.Scope
                        : (ConfigurationScope?) null;
                    var validScopes = new ConfigurationScope?[]
                    {
                        ConfigurationScope.Перечисления, ConfigurationScope.Справочники
                    };
                    if (!validScopes.Contains(scope))
                    {
                        const string messageFormat = "[ПРЕДСТАВЛЕНИЕ] is only supported for [{0}]";
                        throw new InvalidOperationException(string.Format(messageFormat, validScopes.JoinStrings(",")));
                    }
                    var propertyName = scope == ConfigurationScope.Справочники ? "Наименование" : "Порядок";
                    var presentationProperty = GetOrCreatePropertyIfExists(nestedEntity, propertyName);
                    if (presentationProperty == null)
                    {
                        const string messageFormat = "entity [{0}] has no property [{1}]";
                        throw new InvalidOperationException(string.Format(messageFormat,
                            nestedEntity.mapping.QueryTableName, propertyName));
                    }
                    properties.Add(presentationProperty);
                    result = true;
                }
            }
            return result;
        }

        private void EnumProperties(string[] propertyNames, QueryEntity queryEntity, int index,
            List<QueryEntityProperty> result)
        {
            var property = GetOrCreatePropertyIfExists(queryEntity, propertyNames[index]);
            if (property == null)
                return;
            if (index == propertyNames.Length - 1)
                result.Add(property);
            else if (property.mapping.UnionLayout != null)
            {
                var count = result.Count;
                foreach (var p in property.nestedEntities)
                    EnumProperties(propertyNames, p, index + 1, result);
                if (result.Count == count)
                {
                    const string messageFormat = "property [{0}] in [{1}] has multiple types [{2}] " +
                                                 "and none of them has property [{3}]";
                    throw new InvalidOperationException(string.Format(messageFormat,
                        propertyNames[index], propertyNames.JoinStrings("."),
                        property.nestedEntities.Select(x => x.mapping.QueryTableName).JoinStrings(","),
                        propertyNames[index + 1]));
                }
            }
            else if (property.nestedEntities.Count == 1)
                EnumProperties(propertyNames, property.nestedEntities[0], index + 1, result);
            else
            {
                const string messageFormat = "property [{0}] has no table mapping, property path [{1}]";
                throw new InvalidOperationException(string.Format(messageFormat,
                    property.mapping.PropertyName, propertyNames.JoinStrings(".")));
            }
        }

        private QueryEntityProperty GetOrCreatePropertyIfExists(QueryEntity queryEntity, string name)
        {
            foreach (var f in queryEntity.properties)
                if (f.mapping.PropertyName.EqualsIgnoringCase(name))
                    return f;
            if (!queryEntity.mapping.HasProperty(name))
                return null;
            var propertyMapping = queryEntity.mapping.GetByPropertyName(name);
            var property = new QueryEntityProperty(queryEntity, propertyMapping);
            if (propertyMapping.SingleLayout != null)
            {
                if (name == "Ссылка")
                {
                    if (queryEntity.mapping.Type == TableType.TableSection)
                    {
                        var nestedTableName = queryEntity.mapping.QueryTableName;
                        nestedTableName = TableMapping.GetMainQueryNameByTableSectionQueryName(nestedTableName);
                        AddQueryEntity(property, nestedTableName);
                    }
                    else
                        property.nestedEntities.Add(queryEntity);
                }
                else
                {
                    var nestedTableName = propertyMapping.SingleLayout.NestedTableName;
                    if (!string.IsNullOrEmpty(nestedTableName))
                        AddQueryEntity(property, nestedTableName);
                }
            }
            else
                foreach (var t in propertyMapping.UnionLayout.NestedTables)
                    AddQueryEntity(property, t);
            queryEntity.properties.Add(property);
            return property;
        }

        private QueryEntity CreateQueryEntity(QueryEntityProperty referer, string queryName)
        {
            var tableMapping = mappingSource.ResolveTable(queryName);
            return new QueryEntity(tableMapping, referer);
        }

        private void AddQueryEntity(QueryEntityProperty referer, string tableName)
        {
            referer.nestedEntities.Add(CreateQueryEntity(referer, tableName));
        }

        private ISqlElement PatchTableDeclaration(TableDeclarationClause declaration)
        {
            var mainEntity = GetMainQueryEntity(declaration.GetRefName());
            if (!mainEntity.subqueryRequired)
            {
                declaration.Name = mainEntity.queryEntity.mapping.DbTableName;
                return declaration;
            }
            if (Strip(mainEntity.queryEntity) == StripResult.HasNoReferences)
                throw new InvalidOperationException("assertion failure");
            var selectClause = new SelectClause
            {
                Source = GetDeclarationClause(mainEntity.queryEntity)
            };
            if (areas != null)
                selectClause.WhereExpression = new InExpression
                {
                    Column = new ColumnReferenceExpression
                    {
                        Name = mainEntity.queryEntity.GetAreaColumnName(),
                        TableName = GetQueryEntityAlias(mainEntity.queryEntity)
                    },
                    Values = areas
                };
            AddJoinClauses(mainEntity.queryEntity, selectClause);
            AddColumns(mainEntity, selectClause);
            return selectClause;
        }

        private void AddColumns(MainQueryEntity entity, SelectClause target)
        {
            foreach (var f in entity.fields.Values)
            {
                var expression = GetFieldExpression(f, target);
                if (f.invert)
                    expression = new UnaryFunctionExpression
                    {
                        FunctionName = UnaryFunctionName.Not,
                        Argument = expression
                    };
                target.Fields.Add(new SelectField
                {
                    Expression = expression,
                    Alias = f.alias
                });
            }
        }

        private ISqlElement GetFieldExpression(QueryField field, SelectClause selectClause)
        {
            if (field.properties.Length < 1)
                throw new InvalidOperationException("assertion failure");
            if (field.properties.Length == 1)
                return GetPropertyReference(field.properties[0], selectClause);
            var result = new CaseExpression();
            var eqConditions = new List<ISqlElement>();
            foreach (var property in field.properties)
            {
                eqConditions.Clear();
                var entity = property.referer;
                while (entity != null)
                {
                    if (entity.unionCondition != null)
                        eqConditions.Add(entity.unionCondition);
                    entity = entity.referer == null ? null : entity.referer.referer;
                }
                result.Elements.Add(new CaseElement
                {
                    Value = GetPropertyReference(property, selectClause),
                    Condition = eqConditions.Combine()
                });
            }
            return result;
        }

        private ColumnReferenceExpression GetPropertyReference(QueryEntityProperty property, SelectClause selectClause)
        {
            if (property.referer.mapping.IsEnum())
            {
                var enumMappingsJoinClause = CreateEnumMappingsJoinClause(property.referer);
                selectClause.JoinClauses.Add(enumMappingsJoinClause);
                return new ColumnReferenceExpression
                {
                    Name = "enumValueName",
                    TableName = enumMappingsJoinClause.Source.Alias
                };
            }
            return new ColumnReferenceExpression
            {
                Name = property.GetDbColumnName(),
                TableName = GetQueryEntityAlias(property.referer)
            };
        }

        private void AddJoinClauses(QueryEntity entity, SelectClause target)
        {
            foreach (var p in entity.properties)
                foreach (var nestedEntity in p.nestedEntities)
                {
                    if (nestedEntity == entity)
                        continue;
                    var eqConditions = new List<ISqlElement>();
                    if (!nestedEntity.mapping.IsEnum())
                        eqConditions.Add(new EqualityExpression
                        {
                            Left = new ColumnReferenceExpression
                            {
                                Name = nestedEntity.GetAreaColumnName(),
                                TableName = GetQueryEntityAlias(nestedEntity)
                            },
                            Right = new ColumnReferenceExpression
                            {
                                Name = p.referer.GetAreaColumnName(),
                                TableName = GetQueryEntityAlias(p.referer)
                            }
                        });
                    if (p.mapping.UnionLayout != null)
                        eqConditions.Add(nestedEntity.unionCondition = GetUnionCondition(p, nestedEntity));
                    var referenceColumnName = p.mapping.SingleLayout == null
                        ? p.mapping.UnionLayout.ReferenceColumnName
                        : p.mapping.SingleLayout.ColumnName;
                    if (string.IsNullOrEmpty(referenceColumnName))
                    {
                        const string messageFormat = "ref column is not defined for [{0}.{1}]";
                        throw new InvalidOperationException(string.Format(messageFormat,
                            p.referer.mapping.QueryTableName, p.mapping.PropertyName));
                    }
                    eqConditions.Add(new EqualityExpression
                    {
                        Left = new ColumnReferenceExpression
                        {
                            Name = nestedEntity.GetIdColumnName(),
                            TableName = GetQueryEntityAlias(nestedEntity)
                        },
                        Right = new ColumnReferenceExpression
                        {
                            Name = referenceColumnName,
                            TableName = GetQueryEntityAlias(p.referer)
                        }
                    });
                    var joinClause = new JoinClause
                    {
                        Source = new TableDeclarationClause
                        {
                            Name = nestedEntity.mapping.DbTableName,
                            Alias = GetQueryEntityAlias(nestedEntity)
                        },
                        JoinKind = JoinKind.Left,
                        Condition = eqConditions.Combine()
                    };
                    target.JoinClauses.Add(joinClause);
                    AddJoinClauses(nestedEntity, target);
                }
        }

        private static StripResult Strip(QueryEntity queryEntity)
        {
            var result = StripResult.HasNoReferences;
            for (var i = queryEntity.properties.Count - 1; i >= 0; i--)
            {
                var p = queryEntity.properties[i];
                var propertyReferenced = p.referenced;
                for (var j = p.nestedEntities.Count - 1; j >= 0; j--)
                {
                    var nestedEntity = p.nestedEntities[j];
                    if (nestedEntity == queryEntity)
                        continue;
                    if (Strip(nestedEntity) == StripResult.HasNoReferences)
                        p.nestedEntities.RemoveAt(j);
                    else
                        propertyReferenced = true;
                }
                if (propertyReferenced)
                    result = StripResult.HasReferences;
                else
                    queryEntity.properties.RemoveAt(i);
            }
            return result;
        }

        private ISqlElement GetUnionCondition(QueryEntityProperty property, QueryEntity nestedEntity)
        {
            var typeColumnName = property.mapping.UnionLayout.TypeColumnName;
            if (string.IsNullOrEmpty(typeColumnName))
            {
                const string messageFormat = "type column is not defined for [{0}.{1}]";
                throw new InvalidOperationException(string.Format(messageFormat,
                    property.referer.mapping.QueryTableName, property.mapping.PropertyName));
            }
            var tableIndexColumnName = property.mapping.UnionLayout.TableIndexColumnName;
            if (string.IsNullOrEmpty(tableIndexColumnName))
            {
                const string messageFormat = "tableIndex column is not defined for [{0}.{1}]";
                throw new InvalidOperationException(string.Format(messageFormat,
                    property.referer.mapping.QueryTableName, property.mapping.PropertyName));
            }
            return new AndExpression
            {
                Left = new EqualityExpression
                {
                    Left = new ColumnReferenceExpression
                    {
                        Name = typeColumnName,
                        TableName = GetQueryEntityAlias(property.referer)
                    },
                    Right = new LiteralExpression
                    {
                        Value = configurationItemReferenceType,
                        SqlType = SqlType.ByteArray
                    }
                },
                Right = new EqualityExpression
                {
                    Left = new ColumnReferenceExpression
                    {
                        Name = tableIndexColumnName,
                        TableName = GetQueryEntityAlias(property.referer)
                    },
                    Right = new LiteralExpression
                    {
                        Value = nestedEntity.mapping.Index,
                        SqlType = SqlType.ByteArray
                    }
                }
            };
        }

        private class TableDeclarationPatcher : SqlVisitor
        {
            private readonly QueryToSqlTranslator translator;

            public TableDeclarationPatcher(QueryToSqlTranslator translator)
            {
                this.translator = translator;
            }

            public override ISqlElement VisitTableDeclaration(TableDeclarationClause clause)
            {
                return translator.PatchTableDeclaration(clause);
            }
        }

        private class ColumnReferencePatcher : SqlVisitor
        {
            private readonly QueryToSqlTranslator translator;
            private bool isPresentation;
            private SelectPart? currentPart;

            public ColumnReferencePatcher(QueryToSqlTranslator translator)
            {
                this.translator = translator;
            }

            private void WithCurrentPart(SelectPart part, Action handle)
            {
                var oldPart = currentPart;
                currentPart = part;
                handle();
                currentPart = oldPart;
            }

            public override SelectField VisitSelectField(SelectField clause)
            {
                WithCurrentPart(SelectPart.Select, () => base.VisitSelectField(clause));
                return clause;
            }

            public override ISqlElement VisitWhere(ISqlElement element)
            {
                WithCurrentPart(SelectPart.Where, () => base.VisitWhere(element));
                return element;
            }

            public override GroupByClause VisitGroupBy(GroupByClause element)
            {
                WithCurrentPart(SelectPart.GroupBy, () => base.VisitGroupBy(element));
                return element;
            }

            public override JoinClause VisitJoin(JoinClause element)
            {
                WithCurrentPart(SelectPart.Join, () => base.VisitJoin(element));
                return element;
            }

            public override ISqlElement VisitUnary(UnaryFunctionExpression expression)
            {
                isPresentation = expression.FunctionName == UnaryFunctionName.Presentation;
                base.VisitUnary(expression);
                isPresentation = false;
                return expression;
            }

            public override ISqlElement VisitTableDeclaration(TableDeclarationClause clause)
            {
                translator.RegisterMainQueryEntity(clause.GetRefName(), clause.Name);
                return clause;
            }

            public override ISqlElement VisitColumnReference(ColumnReferenceExpression expression)
            {
                //todo remove this shit
                var properties = new[] { expression.TableName }
                    .Concat(expression.Name.Split('.'))
                    .ToArray();

                if (!currentPart.HasValue)
                    throw new InvalidOperationException("assertion failure");
                var queryField = translator.GetOrCreateQueryField(properties, isPresentation,
                    currentPart.GetValueOrDefault());
                expression.Name = queryField.alias ?? queryField.properties[0].GetDbColumnName();
                return expression;
            }
        }

        private static string FormatDateTime(string s)
        {
            var m = dateTimeRegex.Match(s);
            if (!m.Success)
            {
                const string messageFormat = "invalid ДАТАВРЕМЯ arguments [{0}]";
                throw new InvalidOperationException(string.Format(messageFormat, s));
            }
            var date = new DateTime(m.AsInt("year"), m.AsInt("month"), m.AsInt("day"));
            return FormatSqlDate(date);
        }

        private static string FormatSqlDate(DateTime dateTime)
        {
            return "cast('" + dateTime.ToString("yyyy-MM-dd") + "' as date)";
        }

        private string GetQueryEntityAlias(QueryEntity entity)
        {
            return entity.alias ?? (entity.alias = nameGenerator.GenerateTableName());
        }

        private TableDeclarationClause GetDeclarationClause(QueryEntity queryEntity)
        {
            return new TableDeclarationClause
            {
                Name = queryEntity.mapping.DbTableName,
                Alias = GetQueryEntityAlias(queryEntity)
            };
        }

        private class MainQueryEntity
        {
            public readonly QueryEntity queryEntity;
            public readonly Dictionary<string, QueryField> fields = new Dictionary<string, QueryField>();
            public bool subqueryRequired;

            public MainQueryEntity(QueryEntity queryEntity, bool subqueryRequired)
            {
                this.queryEntity = queryEntity;
                this.subqueryRequired = subqueryRequired;
            }
        }

        private class QueryEntity
        {
            public QueryEntity(TableMapping mapping, QueryEntityProperty referer)
            {
                this.mapping = mapping;
                this.referer = referer;
            }

            public readonly TableMapping mapping;
            public readonly QueryEntityProperty referer;
            public string alias;
            public readonly List<QueryEntityProperty> properties = new List<QueryEntityProperty>();
            public ISqlElement unionCondition;

            public string GetAreaColumnName()
            {
                return GetSingleColumnName("ОбластьДанныхОсновныеДанные");
            }

            public string GetIdColumnName()
            {
                return GetSingleColumnName("Ссылка");
            }

            public string GetSingleColumnName(string propertyName)
            {
                return mapping.GetByPropertyName(propertyName).SingleLayout.ColumnName;
            }
        }

        private class QueryEntityProperty
        {
            public readonly QueryEntity referer;
            public readonly PropertyMapping mapping;
            public readonly List<QueryEntity> nestedEntities = new List<QueryEntity>();
            public bool referenced;

            public QueryEntityProperty(QueryEntity referer, PropertyMapping mapping)
            {
                this.referer = referer;
                this.mapping = mapping;
            }

            public string GetDbColumnName()
            {
                return mapping.SingleLayout != null
                    ? mapping.SingleLayout.ColumnName
                    : mapping.UnionLayout.ReferenceColumnName;
            }
        }

        private JoinClause CreateEnumMappingsJoinClause(QueryEntity enumEntity)
        {
            var tableAlias = nameGenerator.GenerateTableName();
            if (!enumEntity.mapping.ObjectName.HasValue)
                throw new InvalidOperationException("assertion failure");
            return new JoinClause
            {
                Source = new TableDeclarationClause
                {
                    Name = "simple1c__enumMappings",
                    Alias = tableAlias
                },
                JoinKind = JoinKind.Left,
                Condition = new AndExpression
                {
                    Left = new EqualityExpression
                    {
                        Left = new ColumnReferenceExpression
                        {
                            Name = "enumName",
                            TableName = tableAlias
                        },
                        Right = new LiteralExpression
                        {
                            Value = enumEntity.mapping.ObjectName.Value.Name
                        }
                    },
                    Right = new EqualityExpression
                    {
                        Left = new ColumnReferenceExpression
                        {
                            Name = "orderIndex",
                            TableName = tableAlias
                        },
                        Right = new ColumnReferenceExpression
                        {
                            Name = enumEntity.GetSingleColumnName("Порядок"),
                            TableName = GetQueryEntityAlias(enumEntity)
                        }
                    }
                }
            };
        }

        private enum StripResult
        {
            HasReferences,
            HasNoReferences
        }

        private enum SelectPart
        {
            Select,
            Where,
            GroupBy,
            Join
        }

        private class NameGenerator
        {
            private readonly Dictionary<string, int> lastUsed = new Dictionary<string, int>();

            public string GenerateTableName()
            {
                return Generate("__nested_table");
            }

            public void Reset()
            {
                lastUsed.Clear();
            }

            public string GenerateColumnName()
            {
                return Generate("__nested_field");
            }

            private string Generate(string prefix)
            {
                int lastUsedForPrefix;
                var number =
                    lastUsed[prefix] = lastUsed.TryGetValue(prefix, out lastUsedForPrefix) ? lastUsedForPrefix + 1 : 0;
                return prefix + number;
            }
        }
    }
}