using System;
using System.Text;
using System.Data;
using System.Data.SqlClient;


namespace Design_Patterns_project.Connection
{
    class MsSqlConnection : IDisposable
    {
        private static readonly MsSqlConnection _instance = new MsSqlConnection();
        private SqlConnection _connection;
        private MsSqlConnectionConfig _config;


        private MsSqlConnection()
        {
            this._connection = new SqlConnection();
        }

        public MsSqlConnection(MsSqlConnectionConfig config)
        {
            this._connection = new SqlConnection();
            this._config = config;
        }

        public static MsSqlConnection GetsInstance()
        {
            return _instance;
        }

        public void SetConfiguration(MsSqlConnectionConfig config)
        {
            this._config = config;
        }


        public void ConnectAndOpen()
        {
            this._connection.ConnectionString = _config.CreateConnectionString();
            this._connection.Open();
        }
        

        public void Dispose()
        {
            this._connection.Close();
            this._connection.Dispose();
            
        }
        private string ReadSingleRow(IDataRecord record, int colsAmount, int colWidht)
        {   
            string recordString = "";
    
            for(int i=0; i<colsAmount; i++){
                string item = record[i].ToString().Trim(' ');
                recordString += item;
                for(int j=0; j<(colWidht - item.Length); j++){
                    recordString += " ";
                }
                recordString += "|";  
            }
            return recordString+"\n";
        }

        // SELECT
        public string ExecuteSelectQuery(string sqlQuery){

            SqlCommand command = new SqlCommand(sqlQuery,this._connection);

            try{
                SqlDataReader dataReader = command.ExecuteReader();

                string output = "";
                int colsAmount = dataReader.FieldCount;
                int colWidht = 12;
    
                // add header
                for(int i=0; i<colsAmount; i++){
                    string colName = dataReader.GetName(i);
                    output += colName;
                    for(int j=0; j<(colWidht - colName.Length); j++){
                        output += " ";
                    }
                    output += "|";  
                }

                output += "\n";
                for(int j=0; j<(colsAmount*colWidht)+colsAmount; j++){output += "-";}
                output += "\n";

                // add records
                while(dataReader.Read()){
                    output += ReadSingleRow((IDataRecord)dataReader,colsAmount,colWidht);
                }

                return output;

            }catch(SqlException ex){
                return HandleSqlException(ex);
            }
            catch(Exception ex){
                return HandleOtherException(ex);
            }
             
        }


        // INSERT,UPDATE,DROP,DELETE, etc.
        public void ExecuteQuery(string sqlQuery){
            
            try{
                SqlCommand command = new SqlCommand(sqlQuery,this._connection);
                int num = command.ExecuteNonQuery();
                Console.WriteLine("Num of edited rows: "+num);

            }catch(SqlException ex){
                Console.WriteLine(HandleSqlException(ex));

            }catch(Exception ex){    
                Console.WriteLine(HandleOtherException(ex)); 
            }
        }

        public string HandleSqlException(SqlException ex){
            StringBuilder errorMessages = new StringBuilder();
            for (int i = 0; i < ex.Errors.Count; i++)
                {
                    errorMessages.Append("\nHandled exception \n"+
                        "Index #" + i + "\n" +
                        "Message: " + ex.Errors[i].Message + "\n" +
                        "LineNumber: " + ex.Errors[i].LineNumber + "\n" +
                        "Source: " + ex.Errors[i].Source + "\n" +
                        "Procedure: " + ex.Errors[i].Procedure + "\n");
                }
                return errorMessages.ToString();
        }

        public string HandleOtherException(Exception ex){
            StringBuilder errorMessages = new StringBuilder();
            errorMessages.Append("\nHandled exception \n"+
                    "Message: "+ex.Message+"\n"+
                    "Source: " +ex.Source+"\n");
            return errorMessages.ToString();
        }

    }

}