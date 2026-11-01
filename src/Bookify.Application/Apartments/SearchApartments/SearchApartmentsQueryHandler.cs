using System.Data;
using Bookify.Application.Abstractions.Data;
using Bookify.Application.Abstractions.Messaging;
using Bookify.Domain.Abstractions;
using Dapper;

namespace Bookify.Application.Apartments.SearchApartments;

internal sealed class SearchApartmentsQueryHandler
    : IQueryHandler<SearchApartmentsQuery, IReadOnlyList<ApartmentResponse>>
{
    private readonly ISqlConnectionFactory _sqlConnectionFactory;

    public SearchApartmentsQueryHandler(ISqlConnectionFactory sqlConnectionFactory)
    {
        _sqlConnectionFactory = sqlConnectionFactory;
    }

    public async Task<Result<IReadOnlyList<ApartmentResponse>>> Handle(
        SearchApartmentsQuery request, 
        CancellationToken cancellationToken)
    {
        if (request.StartDate > request.EndDate)
        {
            return new List<ApartmentResponse>();
        }

        using IDbConnection connection = _sqlConnectionFactory.CreateConnection();

        // SQL CẢI TIẾN: Chỉ check bảng phẳng Read Model (apartment_booked_dates)
        const string sql = """
            SELECT
                a.id AS Id,
                a.name AS Name,
                a.description AS Description,
                a.price_amount AS Price,
                a.price_currency AS Currency,
                a.address_country AS Country,
                a.address_state AS State,
                a.address_zip_code AS ZipCode,
                a.address_city AS City,
                a.address_street AS Street
            FROM apartments AS a
            WHERE NOT EXISTS
            (
                SELECT 1
                FROM apartment_booked_dates AS abd
                WHERE
                    abd.apartment_id = a.id AND
                    abd.date >= @StartDate AND
                    abd.date <= @EndDate
            )
            """;

        IEnumerable<ApartmentResponse> apartments = await connection
            .QueryAsync<ApartmentResponse, AddressResponse, ApartmentResponse>(
                sql,
                (apartment, address) =>
                {
                    apartment.Address = address;
                    return apartment;
                },
                new
                {
                    StartDate = request.StartDate.ToDateTime(TimeOnly.MinValue),
                    EndDate = request.EndDate.ToDateTime(TimeOnly.MinValue)
                },
                splitOn: "Country");

        return apartments.ToList();
    }
}