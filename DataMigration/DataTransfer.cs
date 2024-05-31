using Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataMigration
{
    public static class DataTransferHelper
    {
        public static  List<T> ReadFromSQLServer<T>(DbContext sqlServerContext) where T : class
        {
            return  sqlServerContext.Set<T>().ToList();
        }

        public static void AddRangeToPostgreSQL<T>(DbContext postgreSqlContext, List<T> entities) where T : class
        {
             postgreSqlContext.Set<T>().AddRange(entities);
            
            
        }

        public static  void TransferDataAsync<T>(DbContext sqlServerContext, DbContext postgreSqlContext) where T : class
        {
            var data =  ReadFromSQLServer<T>(sqlServerContext);
             AddRangeToPostgreSQL(postgreSqlContext, data);
        }

        public static async Task TransferDataDynamicAsync(Type entityType, DbContext sqlServerContext, DbContext postgreSqlContext)
        {

            try
            {

                var readMethod = typeof(DataTransferHelper).GetMethod(nameof(ReadFromSQLServer));
                var readgenericMethod = readMethod.MakeGenericMethod(entityType);

                var data = readgenericMethod.Invoke(null, new object[] { sqlServerContext });

                var addMethod = typeof(DataTransferHelper).GetMethod(nameof(AddRangeToPostgreSQL));
                var addGenericMethod = addMethod.MakeGenericMethod(entityType);
                addGenericMethod.Invoke(null, new object[] { sqlServerContext, data });



            }
            catch (Exception e)
            {
                Log.Error($"Error {e.Message}", e);
            }
         
        }
    }

}
