// Program.cs
using Core.Interfaces;
using DataMigration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Serilog;
using System.Reflection;


class Program
{
    static void Main(string[] args)
    {
        try
        {

            #region  Config log and serviceProvider
            ServiceProvider pgProvider, sqlProvider;
            getService(out pgProvider, out sqlProvider);

            DateTime now = DateTime.Now;
            Log.Logger = new LoggerConfiguration()
                                .MinimumLevel.Debug()
                                .WriteTo.Console()
                                .WriteTo.File($"logs/{now.ToString("MM-dd-yyyy-Thh-mm-ss-")}.txt", rollingInterval: RollingInterval.Day)
                                .CreateLogger();
            #endregion

            Log.Information($"Program started at {now}");




            using (var pgrdb = pgProvider.GetService<IAppDbContext>())
            {


                var pgtables = pgrdb.Model.GetEntityTypes()
                     .Select(t => t.GetTableName())
                     .ToList();

                using (var sqldb = sqlProvider.GetService<ISQLServerDbContext>())
                {

                    #region getting basic info
                    var sqltables = sqldb.Model.GetEntityTypes()
                     .Select(t => t.GetTableName())
                     .ToList();

                    Log.Information($"Total Postgresql tables: {pgtables.Count()}");
                    Log.Information($"Total Sql server tables: {sqltables.Count()}");
                    var filteredTables = pgtables.Where(x => !sqltables.Select(x => x.ToLower()).Contains(x.ToLower())).ToList();
                    Log.Information($"Missing tables in Production SQl Server: {String.Join(",", filteredTables)}");
                    #endregion
                    #region getting schema info
                    var pgrSchema = GetTableSchema(pgrdb);
                    var sqlSchema = GetTableSchema(sqldb);
                    Log.Information($"Start Compairing");
                    CompareSchemas(pgrSchema, sqlSchema);
                    #endregion
                    #region cal count of each table on
                    Dictionary<string, int> emptyTables = new Dictionary<string, int>();
                    var fillTables = new Dictionary<string, int>();
                   
                    Log.Information($"Getting Overall records");
                    PrintTotalRecords(sqldb, emptyTables, fillTables);
                    Log.Information($"Printing Empty Table names");
                    foreach (var item in emptyTables)
                    {
                        Console.Write($"{item.Key} |");
                    }

                    Console.WriteLine();
                    Log.Information($"Printing With data Table names");
                    foreach (var item in fillTables)
                    {
                        Console.WriteLine($"{item.Key}:------------------->{item.Value} ");
                    }
                    #endregion
                    UpdateRecord(pgrdb, sqldb, fillTables);



                }


            }

        }
        catch (Exception e)
        {

            Log.Error(e.Message, e);
        }

        Console.ReadLine();


    }
    public static async Task UpdateRecord(DbContext pgadmin, DbContext sqlServer, Dictionary<string, int> filledTable)
    {
        try
        {
            #region config json settings
            var serSetting = new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                ContractResolver = new IgnoreVirtualPropertiesContractResolver()
            };
            #endregion
            #region db context type props
            var sqlServerProps = sqlServer.GetType()
                         .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         .Where(p => p.PropertyType.IsGenericType &&
                                     p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
                         .ToList();




           
            var pgadminProps = pgadmin.GetType()
                            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                            .Where(p => p.PropertyType.IsGenericType &&
                                        p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
                            .ToDictionary(x => x.Name, x => x);

            #endregion
            foreach (var sqlProp in sqlServerProps)
            {

                #region getting record from sql server and onvert it into json

                var sqlEntityType = sqlProp.PropertyType.GetGenericArguments().First();




                var toListMethod = typeof(DataTransferHelper).GetMethods()
                                                      .First(m => m.Name == "ReadFromSQLServer")
                                                      .MakeGenericMethod(sqlEntityType);

                var records = toListMethod.Invoke(null, new[] { sqlServer });

               
                int length = 0;
                if (records is IEnumerable<object> enumerable)
                {
                    length = enumerable.Count();
                }
                if (length == 0)
                {
                    Log.Information($"{sqlProp.Name} is already empty");
                    continue;
                }
                else
                {
                    Log.Information($"{sqlProp.Name} info adding");
                }

                var jsonResponse = Newtonsoft.Json.JsonConvert.SerializeObject(records, serSetting);
                #endregion

                #region adding in postgresql all sql server record
                if (pgadminProps.TryGetValue(sqlProp.Name, out var pgprop))
                {

                    var pgEntityType = pgprop.PropertyType.GetGenericArguments().First();
                    var pgInstance = pgprop.GetValue(pgadmin);
                    JsonConvert.DeserializeObject("", serSetting);
                    MethodInfo deserializeMethod = typeof(JsonConvert)
                    .GetMethods()
                    .First(m => m.Name == "DeserializeObject" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string))
                    .MakeGenericMethod(typeof(List<>).MakeGenericType(pgEntityType));

                   
                    object result = deserializeMethod.Invoke(null, new object[] { jsonResponse });

                    var addRangeMethod = typeof(DataTransferHelper).GetMethods()
                                              .First(m => m.Name == "AddRangeToPostgreSQL")
                                              .MakeGenericMethod(pgEntityType);



                    addRangeMethod.Invoke(null, new[] { pgadmin, result });





                }
                else
                {
                    Log.Information($"{sqlProp.Name} not found in postgresql");
                }

                #endregion













            }
            pgadmin.SaveChanges();
            Log.Information("Data is saved");
        }
        catch (Exception e)
        {

            Log.Error(e.Message);
        }
    }



    public static void PrintTotalRecords(DbContext db, Dictionary<string, int> emptyTables, Dictionary<string, int> fillTables)

    {
        
        var dbSets = db.GetType()
                         .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         .Where(p => p.PropertyType.IsGenericType &&
                                     p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
                         .ToList();





        foreach (var dbSet in dbSets)
        {
            
            var entityType = dbSet.PropertyType.GetGenericArguments().First();

            
            var dbSetInstance = dbSet.GetValue(db);

          
            var countMethod = typeof(Queryable).GetMethods()
                                               .First(m => m.Name == "Count" &&
                                                           m.GetParameters().Length == 1)
                                               .MakeGenericMethod(entityType);

            
            var count = (int)countMethod.Invoke(null, new[] { dbSetInstance });
            if (count == 0)
            {
                emptyTables.Add(entityType.Name, count);
            }
            else
            {
                fillTables.Add(entityType.Name, count);
            }

           
        }
    }
    static Dictionary<string, Dictionary<string, string>> GetTableSchema(DbContext context)
    {
       
        var schema = new Dictionary<string, Dictionary<string, string>>();

        var tables = context.Model.GetEntityTypes()
            .Select(t => new
            {
                TableName = t.GetTableName(),
                Columns = t.GetProperties().ToDictionary(p => p.Name, p => p.ClrType.Name)
            });

        foreach (var table in tables)
        {
            schema[table.TableName] = table.Columns;
        }

        return schema;
    }
    static void CompareSchemas(Dictionary<string, Dictionary<string, string>> postgresTables, Dictionary<string, Dictionary<string, string>> sqlServerTables)
    {
        var allTables = new HashSet<string>(postgresTables.Keys.Concat(sqlServerTables.Keys));

        foreach (var table in allTables)
        {
            Console.WriteLine($"Checking Table: {table}");

            if (!postgresTables.ContainsKey(table))
            {
                Console.WriteLine("--Missing in PostgreSql---");
                continue;
            }

            if (!sqlServerTables.ContainsKey(table))
            {
                Console.WriteLine("--Missing in Prod Sql Server---");
                continue;
            }

            var postgresColumns = postgresTables[table];
            var sqlServerColumns = sqlServerTables[table];
            var allColumns = new HashSet<string>(postgresColumns.Keys.Concat(sqlServerColumns.Keys));

            foreach (var column in allColumns)
            {
                if (!postgresColumns.ContainsKey(column))
                {
                    Console.WriteLine($"  Column {column}: --Missing in PostgreSql--");
                }
                else if (!sqlServerColumns.ContainsKey(column))
                {
                    Console.WriteLine($"  Column {column}: --Missing in SQL Server--");
                }
                else if (postgresColumns[column] != sqlServerColumns[column])
                {
                    Console.WriteLine($"  Column {column}: Type mismatch (PostgreSQL: {postgresColumns[column]}, SQL Server: {sqlServerColumns[column]})");
                }

            }
        }
    }


    private static void getService(out ServiceProvider pgProvider, out ServiceProvider sqlProvider)
    {
        pgProvider = new ServiceCollection()
            .AddDbContext<IAppDbContext>(options =>
                options.UseNpgsql("Host=127.0.0.1;Port=5432;Database=PSO_BI_Dev;Username=postgres;Password=Qbs!23"))

            .BuildServiceProvider();
       
        sqlProvider = new ServiceCollection()

            .AddDbContext<ISQLServerDbContext>(options =>
                options.UseSqlServer("Data Source=QBS-WEBAPP-NOMA\\SQLEXPRESS;Initial Catalog=PSO_BI_Dev;Integrated Security=True;TrustServerCertificate=True;"))
            .BuildServiceProvider();
    }
}
