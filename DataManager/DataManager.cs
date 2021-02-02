﻿using System;
using System.Data;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Collections.Generic;
using System.Collections;
using System.Reflection;
using System.Data.SqlClient;
using Design_Patterns_project.Relationships;
using Design_Patterns_project.SqlCommands;
using Design_Patterns_project.Connection;
using Design_Patterns_project.Attributes;

namespace Design_Patterns_project
{
    class DataManager
    {
        DataMapper _dataMapper = new DataMapper();
        MsSqlConnection _msSqlConnection;
        QueryBuilder _queryBuilder = new QueryBuilder();
        TableInheritance _tableInheritance = new TableInheritance();
        RelationshipFinder _relationshipFinder = new RelationshipFinder();

        public DataManager(string serverName, string databaseName, string user, string password)
        {
            MsSqlConnectionConfig config = new MsSqlConnectionConfig(serverName, databaseName, user, password);
            this._msSqlConnection = new MsSqlConnection(config);
        }

        public DataManager(string serverName, string databaseName)
        {
            MsSqlConnectionConfig config = new MsSqlConnectionConfig(serverName, databaseName);
            this._msSqlConnection = new MsSqlConnection(config);
        }

        private string GetMergedNames(string name1, string name2)
        {
            return string.Compare(name1, name2) < 0 ? name1 + name2 : name2 + name1;
        }

        public void CreateTable(Object instance)
        {
            CreateTable(instance, "", "");
        }

        private void CreateTable(Object instance, string parentTableName, string foreignKeyName)
        {
            List<Tuple<string, Object>> columnsAndValuesList = _dataMapper.GetColumnsAndValues(instance);
            string primaryKeyName = _dataMapper.FindPrimaryKeyFieldName(instance.GetType());
            string tableName = _dataMapper.GetTableName(instance.GetType());
            string query;

            if (parentTableName.Equals(""))
            {
                query = _queryBuilder.CreateCreateTableQuery(tableName, columnsAndValuesList, primaryKeyName);
            }
            else
            {
                Object foreignKey = _dataMapper.FindPrimaryKey(instance);
                Dictionary<string, Tuple<string, Object>> tableAndForeignKey = new Dictionary<string, Tuple<string, Object>>
                { {parentTableName, new Tuple<string, Object> (foreignKeyName, foreignKey)} };
                query = _queryBuilder.CreateCreateTableQuery(tableName, columnsAndValuesList, primaryKeyName, tableAndForeignKey);
            }

            PerformQuery(query);

            // foreign key mapping
            List<Relationship> oneToOne = _relationshipFinder.FindOneToOne(instance);
            List<Relationship> oneToMany = _relationshipFinder.FindOneToMany(instance);
            // association table mapping
            List<Relationship> manyToMany = _relationshipFinder.FindManyToMany(instance);

            if (oneToOne.Count != 0)
            {
                foreach (var relation in oneToOne)
                {
                    PropertyInfo property = relation._secondMember;
                    MethodInfo strGetter = property.GetGetMethod(nonPublic: true);
                    Object value = strGetter.Invoke(instance, null);
                    CreateTable(value, tableName, primaryKeyName);
                }
            }

            if (oneToMany.Count != 0)
            {
                foreach (var relation in oneToMany)
                {
                    PropertyInfo property = relation._secondMember;
                    MethodInfo strGetter = property.GetGetMethod(nonPublic: true);
                    var values = strGetter.Invoke(instance, null);
                    IList valueList = values as IList;

                    CreateTable(valueList[0], tableName, primaryKeyName);
                }
            }

            if (manyToMany.Count != 0)
            {
                foreach (var relation in manyToMany)
                {
                    PropertyInfo property = relation._secondMember;
                    MethodInfo strGetter = property.GetGetMethod(nonPublic: true);
                    var values = strGetter.Invoke(instance, null);
                    IList valueList = values as IList;
                    var secondInstance = valueList[0];

                    string memberTableName = _dataMapper.GetTableName(secondInstance.GetType());
                    string memberTableKeyName = _dataMapper.FindPrimaryKeyFieldName(secondInstance.GetType());
                    string mergedTablesName = GetMergedNames(tableName, memberTableName);
                    Object firstPrimaryKey = _dataMapper.FindPrimaryKey(instance);
                    Object secondPrimaryKey = _dataMapper.FindPrimaryKey(secondInstance);

                    Dictionary<string, Tuple<string, Object>> tablesAndForeignKeys = new Dictionary<string, Tuple<string, Object>> {
                        { tableName, new Tuple<string, Object>(primaryKeyName, firstPrimaryKey) },
                        { memberTableName, new Tuple<string, Object> (memberTableKeyName, secondPrimaryKey) } };

                    CreateTable(secondInstance);
                    CreateAssociationTable(mergedTablesName, tablesAndForeignKeys);
                }
            }
        }

        private void CreateAssociationTable(string tableName, Dictionary<string, Tuple<string, Object>> tablesAndForeignKeys)
        {
            string query = _queryBuilder.CreateCreateTableQuery(tableName, null, "", tablesAndForeignKeys);

            PerformQuery(query);
        }

        private void CreateTable(Type objectType, List<PropertyInfo> inheritedProperties)
        {
            List<Tuple<string, Object>> columnsBasedOnProperties = _dataMapper.GetInheritedColumnsAndValues(inheritedProperties);
            string tableName = _dataMapper.GetTableName(objectType);

            PropertyInfo primaryProperty = inheritedProperties.Find(x => x.GetCustomAttribute(typeof(PKeyAttribute), false) != null);
            string primaryKeyName = primaryProperty != null ?
                (((ColumnAttribute)primaryProperty.GetCustomAttribute(typeof(ColumnAttribute), false))._columnName ?? primaryProperty.Name)
                : "";
            string query = _queryBuilder.CreateCreateTableQuery(tableName, columnsBasedOnProperties, primaryKeyName);

            PerformQuery(query);
        }

        public string Select(Type type, List<SqlCondition> listOfSqlCondition)
        {
            string tableName = _dataMapper.GetTableName(type);

            if (!_msSqlConnection.CheckIfTableExists(tableName))
            {
                Type rootHierarchyType = _tableInheritance.GetMainType(type);
                tableName = _dataMapper.GetTableName(rootHierarchyType);
            }

            string selectQuery = _queryBuilder.CreateSelectQuery(tableName, listOfSqlCondition);
            string selectQueryOutput = _msSqlConnection.ExecuteSelectQuery(selectQuery, tableName);

            return selectQueryOutput;
        }


        public List<object> SelectObjects(Type type, List<SqlCondition> listOfSqlCondition){

            List<object> selectedObjects = new List<object>{};

            string tableName = _dataMapper.GetTableName(type);

            // if (!_msSqlConnection.CheckIfTableExists(tableName))
            // {
            //     Type rootHierarchyType = _tableInheritance.GetMainType(type);
            //     tableName = _dataMapper.GetTableName(rootHierarchyType);
            // }

            string selectQuery = _queryBuilder.CreateSelectQuery(tableName, listOfSqlCondition);
 
            SqlDataReader dataReader = _msSqlConnection.ExecuteObjectSelect(selectQuery, tableName);

            
            while(dataReader.Read())
            {
                IDataRecord records = (IDataRecord)dataReader;
                object[] parameters = FetchDataFromRecord(records);
                Object o = Activator.CreateInstance(type,parameters);
                selectedObjects.Add(o);
            }

            dataReader.Close();
            _msSqlConnection.Dispose();

            return selectedObjects;
        }

        private object[] FetchDataFromRecord(IDataRecord record)
        {
            object[] parameters = new object[record.FieldCount];

            for (int i = 0; i < record.FieldCount; i++)
            {
                parameters[i] = record[i];
            }
            
            return parameters;
        }


        public object SelectById(Object obj, object primaryKey)
        {
            string tableName = _dataMapper.GetTableName(obj.GetType());
            string primaryKeyName = _dataMapper.FindPrimaryKeyFieldName(obj.GetType());

            List<SqlCondition> conditions = new List<SqlCondition> { SqlCondition.Equals(primaryKeyName, primaryKey) };
            String query = _queryBuilder.CreateSelectQuery(tableName, conditions);

            _msSqlConnection.ConnectAndOpen();

            SqlCommand command = new SqlCommand(query, _msSqlConnection.GetConnection());
            SqlDataReader reader = command.ExecuteReader();
            Object mappedObject = _dataMapper.MapTableIntoObject(obj, reader);

            _msSqlConnection.Dispose();

            return mappedObject;
        }

        public void Insert(Object obj, Tuple<string, object> parentKey = null)
        {
            if (obj != null){

                string tableName = _dataMapper.GetTableName(obj.GetType());
                List<Tuple<string, object>> columnsAndValuesList;
                object primaryKey;
                object primaryKeyName;

                if (_msSqlConnection.CheckIfTableExists(tableName))
                {
                    // concrete table inheritance
                    if ((_msSqlConnection.GetColumnNamesFromTable(tableName)).Count == (DataMapper.GetTypeAllProperties(obj.GetType())).Length)
                    {
                        columnsAndValuesList = _dataMapper.GetColumnsAndValues(obj, true);
                        primaryKey = _dataMapper.FindPrimaryKey(obj, true);
                        primaryKeyName = _dataMapper.FindPrimaryKeyFieldName(obj.GetType(), true);
                    }
                    // class table inheritance or normal insert on single class
                    else
                    {
                        columnsAndValuesList = _dataMapper.GetColumnsAndValues(obj);
                        primaryKey = _dataMapper.FindPrimaryKey(obj);
                        primaryKeyName = _dataMapper.FindPrimaryKeyFieldName(obj.GetType());
                    }
                }
                // single table inheritance
                else
                {
                    Type rootHierarchyType = _tableInheritance.GetMainType(obj.GetType());
                    tableName = _dataMapper.GetTableName(rootHierarchyType);
                    columnsAndValuesList = _dataMapper.GetColumnsAndValues(obj, true);
                    primaryKey = _dataMapper.FindPrimaryKey(obj, true);
                    primaryKeyName = _dataMapper.FindPrimaryKeyFieldName(obj.GetType(), true);
                }

                // relationships lists
                List<Relationship> oneToOne = _relationshipFinder.FindOneToOne(obj);
                List<Relationship> oneToMany = _relationshipFinder.FindOneToMany(obj);
                List<Relationship> manyToMany = _relationshipFinder.FindManyToMany(obj);

                string insertQuery;

                if (parentKey != null)
                {
                    columnsAndValuesList.Add(parentKey);
                    insertQuery = _queryBuilder.CreateInsertQuery(tableName, columnsAndValuesList);
                }
                else
                {
                    insertQuery = _queryBuilder.CreateInsertQuery(tableName, columnsAndValuesList);
                }

                PerformQuery(insertQuery);

                if (oneToOne.Count != 0)
                {
                    foreach (var relation in oneToOne)
                    {
                        PropertyInfo propertyObj = relation._secondMember;
                        MethodInfo getter = propertyObj.GetGetMethod(nonPublic: true);
                        Object secondMemberObject = getter.Invoke(obj, null);
                        Tuple<string, object> parentKeyTuple = new Tuple<string, object>(tableName + (string)primaryKeyName, primaryKey);

                        Insert(secondMemberObject, parentKeyTuple);
                    }
                }

                if (oneToMany.Count != 0)
                {
                    foreach (var relation in oneToMany)
                    {
                        PropertyInfo propertyObj = relation._secondMember;
                        MethodInfo getter = propertyObj.GetGetMethod(nonPublic: true);
                        Object secondMemberObject = getter.Invoke(obj, null);
                        IList secondMemberObjectList = secondMemberObject as IList;

                        foreach (var item in secondMemberObjectList)
                        {
                            Tuple<string, object> parentKeyTuple = new Tuple<string, object>(tableName + (string)primaryKeyName, primaryKey);
                            Insert(item, parentKeyTuple);
                        }
                    }
                }

                if (manyToMany.Count != 0)
                {
                    foreach (var relation in manyToMany)
                    {
                        PropertyInfo propertyObj = relation._secondMember;
                        MethodInfo getter = propertyObj.GetGetMethod(nonPublic: true);
                        Object secondMemberObject = getter.Invoke(obj, null);
                        IList secondMemberObjectList = secondMemberObject as IList;

                        foreach (var item in secondMemberObjectList)
                        {
                            Object secondMemberKey = _dataMapper.FindPrimaryKey(item);
                            Object secondMemberKeyName = _dataMapper.FindPrimaryKeyFieldName(item.GetType());
                            string secondMemberTableName = _dataMapper.GetTableName(item.GetType());

                            Insert(item);

                            Tuple<string, object> oneTableKey = new Tuple<string, object>(tableName + primaryKeyName, primaryKey);
                            Tuple<string, object> secondTableKey = new Tuple<string, object>(secondMemberTableName + secondMemberKeyName, secondMemberKey);
                            List<Tuple<string, object>> keysAndValues = new List<Tuple<string, object>> { oneTableKey, secondTableKey };

                            string associationTableName = GetMergedNames((string)tableName, (string)secondMemberTableName);
                            string intoAssocTableInsertQuery = _queryBuilder.CreateInsertQuery(associationTableName, keysAndValues);

                            PerformQuery(intoAssocTableInsertQuery);
                        }
                    }
                }

            }
            
        }

        public void Delete(Object obj)
        {
            string primaryKeyName;
            Object primaryKey;
            string tableName = _dataMapper.GetTableName(obj.GetType());

            if (_msSqlConnection.CheckIfTableExists(tableName))
            {
                // concrete table inheritance
                if ((_msSqlConnection.GetColumnNamesFromTable(tableName)).Count == (DataMapper.GetTypeAllProperties(obj.GetType())).Length)
                {
                    primaryKey = _dataMapper.FindPrimaryKey(obj, true);
                    primaryKeyName = _dataMapper.FindPrimaryKeyFieldName(obj.GetType(), true);
                }
                // class table inheritance or normal insert on single class
                else
                {
                    primaryKey = _dataMapper.FindPrimaryKey(obj);
                    primaryKeyName = _dataMapper.FindPrimaryKeyFieldName(obj.GetType());
                }
            }
            // single table inheritance
            else
            {
                Type rootHierarchyType = _tableInheritance.GetMainType(obj.GetType());
                tableName = _dataMapper.GetTableName(rootHierarchyType);
                primaryKey = _dataMapper.FindPrimaryKey(obj, true);
                primaryKeyName = _dataMapper.FindPrimaryKeyFieldName(obj.GetType(), true);
            }

            List<SqlCondition> listOfCriteria = new List<SqlCondition> { SqlCondition.Equals(primaryKeyName, primaryKey) };
            string query = _queryBuilder.CreateDeleteQuery(tableName, listOfCriteria);

            PerformQuery(query);
        }

        public void Delete(string tableName, List<SqlCondition> listOfCriteria)
        {
            String query = _queryBuilder.CreateDeleteQuery(tableName, listOfCriteria);
            PerformQuery(query);
        }

        public void Update(Type type, List<Tuple<string, object>> valuesToSet, List<SqlCondition> conditions)
        {
            string tableName = _dataMapper.GetTableName(type);

            if (!_msSqlConnection.CheckIfTableExists(tableName))
            {
                Type rootHierarchyType = _tableInheritance.GetMainType(type);
                tableName = _dataMapper.GetTableName(rootHierarchyType);
            }

            string updateQuery = _queryBuilder.CreateUpdateQuery(tableName,valuesToSet, conditions);
            PerformQuery(updateQuery);
        }

        public void Inherit(List<Object> lastMembersOfInheritanceHierarchy, int mode)
        {
            try
            {
                TryToInherit(lastMembersOfInheritanceHierarchy, mode);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }

        public void TryToInherit(List<Object> lastMembersOfInheritanceHierarchy, int mode)
        {
            switch (mode)
            {
                case 0: //SingleInheritance
                    List<PropertyInfo> memberList = _tableInheritance.InheritSingle(lastMembersOfInheritanceHierarchy);
                    Type mainType = _tableInheritance.GetMainType((lastMembersOfInheritanceHierarchy[0]).GetType());
                    CreateTable(mainType, memberList);

                    break;
                case 1: //ClassInheritance
                    Dictionary<Type, List<PropertyInfo>> typeMap = _tableInheritance.InheritClass(lastMembersOfInheritanceHierarchy);

                    foreach (var pair in typeMap)
                    {
                        CreateTable(pair.Key, pair.Value);
                    }

                    break;
                case 2: //ConcreteInheritance
                    Dictionary<Type, List<PropertyInfo>> singleMap = _tableInheritance.InheritConcrete(lastMembersOfInheritanceHierarchy);

                    foreach (var pair in singleMap)
                    {
                        CreateTable(pair.Key, pair.Value);
                    }

                    break;
                default:
                    throw new ArgumentException("Incorrect value", nameof(mode));
            }
        }

        public void PerformQuery(string query)
        {
            _msSqlConnection.ConnectAndOpen();
            _msSqlConnection.ExecuteQuery(query);
            _msSqlConnection.Dispose();
        }


    }
}
