using System.Net;
using BuildingBlocks.Exception;

namespace Passenger.Exceptions;

public class PassengerNotFoundException : AppException
{
    public PassengerNotFoundException() : base("Passenger not found!", HttpStatusCode.NotFound)
    {
    }
}
