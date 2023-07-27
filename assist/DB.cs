using MySql.Data.MySqlClient;
using System.Data;
using System.Text;

namespace maxoms.assist
{
    internal class DB
    {
        #region Get config information from tb_service_config
        public static void GetTbServiceConfig(string iconf)
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(iconf))
                {
                    con.Open();

                    string query = "SELECT field_key, field_value, last_update FROM tb_service_config;";
                    using (MySqlCommand cmd = new MySqlCommand(query, con))
                    {
                        using (MySqlDataAdapter sda = new MySqlDataAdapter())
                        {
                            sda.SelectCommand = cmd;

                            using (DataSet ds = new DataSet())
                            {
                                sda.Fill(ds);
                                DataTable dt = ds.Tables[0];

                                var sb = new StringBuilder();
                                sb.Append("Service Config Setting");

                                for (int i = 0; i < dt.Rows.Count; i++)
                                {
                                    var row     = dt.Rows[i].ItemArray;
                                    string key  = row[0].ToString().Trim();
                                    string va   = row[1].ToString().Trim();
                                    sb.AppendLine($" - {key}={va} ({((DateTime)row[2]).ToString("yyyy-MM-dd HH:mm:ss")})");

                                    Sys.IConfDB[key] = va;
                                }

                                Sys.ILog.Information(sb.ToString());
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Sys.ILog.Error(ex.ToString());
            }
        }
        #endregion
    }
}