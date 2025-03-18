using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Text.Json;
using System.Reflection;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using Azure;
using Azure.AI.OpenAI;  
using OpenAI.Chat;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Company.Function
{
    public class HttpTrigger1
    {
        private readonly ILogger<HttpTrigger1> _logger;
        private readonly string _sqlConn = "Server={YOUR CRM URL}.crm.dynamics.com;Database=your-crm-db;Encrypt=True;TrustServerCertificate=False;Persist Security Info=False";
        public HttpTrigger1(ILogger<HttpTrigger1> logger)
        {
            _logger = logger;
        }

        [Function("HttpTrigger1")]
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequest req)
        {
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic bodyData = JsonConvert.DeserializeObject(requestBody);
            // Extract the JSON data and the JMESPath expression from the body
            string userQuestion = bodyData?.userQuestion;
            string extraTables = bodyData?.extraTables; //Format should be comma seperated list starting with a comma,"

            _logger.LogInformation("C# HTTP trigger function processed a request {userQuestion}", userQuestion);

            var dbSchema = GetDBSchema();
            var dbSchemaAsJSON = JsonConvert.SerializeObject(dbSchema, Formatting.Indented);

            var sqlResponse = await GenerateSQLQuery(dbSchemaAsJSON, userQuestion);

            var sqlRan = JsonConvert.SerializeObject(RunSQLQuery(sqlResponse), Formatting.Indented);

            return new OkObjectResult(sqlRan);
        }

        [Function("GenerateSQLQuery")]
        public async Task<IActionResult> GenerateSQLQuery(string dbSchema, string userQuestion)
        {
                                    
            var endpoint = new Uri(Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT"));
            var credential = new AzureKeyCredential(Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY"));
            var model = "gpt-4o";

            // Initialize the AzureOpenAIClient
            AzureOpenAIClient azureClient = new(endpoint, credential); 

            // Initialize the ChatClient with the specified deployment name
            ChatClient chatClient = azureClient.GetChatClient(model);  
            
            // Create a list of chat messages
            var messages = new List<ChatMessage>
            {
                new SystemChatMessage("You are an AI assistant that receives a SQL schema and using the user's question, create NLP to SQL. Only output the SQL query and no other text or null"),
                new UserChatMessage($"Generate a SQL query that would answer the user's question using only this SQL Schema: {dbSchema}. Here's the user's question: {userQuestion}")
            };
            
            // Create chat completion options     
            var options = new ChatCompletionOptions {  
                Temperature = (float)0.7,  
                MaxOutputTokenCount = 800,  
                
                TopP=(float)0.95,  
                FrequencyPenalty=(float)0,  
                PresencePenalty=(float)0
            }; 
        
            ChatCompletion completion = await chatClient.CompleteChatAsync(messages, options);  

            if (completion != null)
            {
                _logger.LogInformation("gptResponse", completion);
            }
            else
            {
                Console.WriteLine("No response received.");
            }

            return new OkObjectResult(completion);
        }

        private DataTable GetDBSchema(string? extraTables = null){

            var GetSchemaQuery = $"SELECT s.name AS SchemaName, t.name AS TableName, c.name AS ColumnName, ty.name AS DataType FROM sys.columns c JOIN sys.tables t ON c.object_id = t.object_id JOIN sys.schemas s ON t.schema_id = s.schema_id JOIN sys.types ty ON c.user_type_id = ty.user_type_id WHERE t.name IN ('tot_outdooractivity', 'tot_instructor'{extraTables}) ORDER BY s.name, t.name, c.column_id;";
            
            return QueryDatabase(GetSchemaQuery);
        }


        private DataTable RunSQLQuery(IActionResult sqlQuery){

            var okResult = sqlQuery as OkObjectResult;
            var json = JObject.FromObject(okResult.Value);
            // Extract only the SQL query from "Value.Content[0].Text"
            string sqlProperty = json["Content"][0]["Text"].ToString();

            string cleanSQL = Regex.Replace(sqlProperty, @"```sql\s*|\s*```", "").Trim();

            return QueryDatabase(cleanSQL);
        }

        private DataTable QueryDatabase(string query)
        {
            var sqlConn = "Server=org2004329f.crm.dynamics.com;Database=your-crm-db;Encrypt=True;TrustServerCertificate=False;Persist Security Info=False";
            var token = GetAccessToken();
            using (var conn = new SqlConnection(sqlConn))
            {
                conn.AccessToken = token;
                conn.Open();
 
                var cmd = conn.CreateCommand();
                cmd.CommandText = query;
                var reader = cmd.ExecuteReader();
                var dataTable = new DataTable();
                dataTable.Load(reader);
                conn.Close();
 
                return dataTable;
            }
        }
 
        public string GetAccessToken()
        {
            var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Post, "https://login.microsoftonline.com/{TENANT ID}/oauth2/token");
            var collection = new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = Environment.GetEnvironmentVariable("CLIENT_ID"),
                ["client_secret"] = Environment.GetEnvironmentVariable("CLIENT_SECRET"),
                ["resource"] = "https://{YOUR CRM BASE}.crm.dynamics.com"
            };
            var content = new FormUrlEncodedContent(collection);
            request.Content = content;
            var response = client.SendAsync(request).Result;
            response.EnsureSuccessStatusCode();
            var result = response.Content.ReadAsStringAsync().Result;
 
            return ExtractAccessToken(result);
        }

        public static IEnumerable<T> ToModels<T>(DataTable table) where T : new()
        {
            foreach (DataRow row in table.Rows)
            {
                T item = new T();
                foreach (DataColumn column in table.Columns)
                {
                    PropertyInfo prop = typeof(T).GetProperty(column.ColumnName);
                    if (prop != null && row[column] != DBNull.Value)
                    {
                        prop.SetValue(item, row[column]);
                    }
                }
                yield return item;
            }
        }
 
        public string ExtractAccessToken(string input)
        {
            var jsonDoc = JsonDocument.Parse(input);
            if (jsonDoc.RootElement.TryGetProperty("access_token", out JsonElement accessTokenElement))
            {
                return accessTokenElement.GetString();
            }
            else
            {
                throw new ArgumentException("Access token not found in the input string.");
            }
        }
    }
}
