using System.Xml.Linq;

using Microsoft.AspNetCore.DataProtection.Repositories;

using Npgsql;

using Trading.Service.Persistence;

namespace Trading.Service.Identity;

/// <summary>Keeps the keys that protect login cookies in Postgres, so logins survive restarts and work on every instance.</summary>
internal sealed class PostgresXmlRepository(NpgsqlDataSource dataSource, DatabaseSchema schema) : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        schema.Ensure();
        using var command = dataSource.CreateCommand("select xml from data_protection_keys order by created_at");
        using var reader = command.ExecuteReader();
        var elements = new List<XElement>();
        while (reader.Read())
        {
            elements.Add(XElement.Parse(reader.GetString(0)));
        }

        return elements;
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        schema.Ensure();
        using var command = dataSource.CreateCommand("insert into data_protection_keys (id, xml) values ($1, $2) on conflict (id) do nothing");
        command.Parameters.AddWithValue(friendlyName);
        command.Parameters.AddWithValue(element.ToString(SaveOptions.DisableFormatting));
        command.ExecuteNonQuery();
    }
}
