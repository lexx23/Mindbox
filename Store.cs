using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Controllers
{
    internal interface IRedisClient
    {
        int Get(string type);
        void Set(string type, int current);
    }
    
    public interface IFiguresStorage
    {
        bool CheckIfAvailable(string type, int count);
        void Reserve(string type, int count);
        void RemoveReserve(string type, int count);
    }
    
    public class FiguresStorage: IFiguresStorage
    {
        // корректно сконфигурированный и готовый к использованию клиент Редиса
        private IRedisClient RedisClient { get; }
	
        public bool CheckIfAvailable(string type, int count)
        {
            return RedisClient.Get(type) >= count;
        }

        public void Reserve(string type, int count)
        {
            var current = RedisClient.Get(type);

            RedisClient.Set(type, current - count);
        }
		
        public void RemoveReserve(string type, int count)
        {
            var current = RedisClient.Get(type);

            RedisClient.Set(type, current + count);
        }
    }

    
    interface IValidator
    {
        void Validate();
    }

    public interface IArea
    {
        double GetArea();
    }


    interface ICircle : IArea
    {
        float Radius { get; }
    }

    interface ITriangle : IArea
    {
        float SideA { get; }
        float SideB { get; }
        float SideC { get; }
    }

    interface ISquare : IArea
    {
        float SideA { get; }
        float SideB { get; }
    }

    class Square : ISquare, IValidator
    {
        public FigureTypes Type = FigureTypes.Square;
        public float SideA { get; }
        public float SideB { get; }

        public Square(float sideA, float sideB)
        {
            SideA = sideA;
            SideB = sideB;

            Validate();
        }

        public double GetArea() => SideA * SideA;

        public void Validate()
        {
            if (SideA <= 0)
                throw new ArgumentOutOfRangeException("Square restrictions not met");

            if (Math.Abs(SideA - SideB) > 0)
                throw new ArgumentOutOfRangeException("Square restrictions not met");
        }
    }

    class Triangle : ITriangle, IValidator
    {
        public float SideA { get; }
        public float SideB { get; }
        public float SideC { get; }

        public Triangle(float sideA, float sideB, float sideC)
        {
            SideA = sideA;
            SideB = sideB;
            SideC = sideC;

            Validate();
        }

        public void Validate()
        {
            if (CheckTriangleInequality(SideA, SideB, SideC)
                && CheckTriangleInequality(SideB, SideA, SideC)
                && CheckTriangleInequality(SideC, SideB, SideA))
                return;
            throw new ArgumentOutOfRangeException("Triangle restrictions not met");
        }

        private static bool CheckTriangleInequality(float a, float b, float c) => a < b + c;

        public double GetArea()
        {
            var p = (SideA + SideB + SideC) / 2;
            return Math.Sqrt(p * (p - SideA) * (p - SideB) * (p - SideC));
        }
    }

    public class Circle : ICircle, IValidator
    {
        public float Radius { get; }

        public Circle(float radius)
        {
            Radius = radius;
            Validate();
        }

        public void Validate()
        {
            if (Radius <= 0)
                throw new ArgumentOutOfRangeException(nameof(Radius));
        }

        public double GetArea() => Math.PI * Radius * Radius;
    }


    public class CartPosition
    {
        public IArea Figure { get; }
        public int Count { get; }

        public CartPosition(IArea figure, int count)
        {
            Figure = figure;
            Count = count;
        }
    }

    public class Cart : IRequest<decimal>
    {
        public IList<CartPosition> Positions { get; }

        public Cart(IList<CartPosition> positions)
        {
            if (positions == null || positions.Count == 0)
                throw new ArgumentNullException(nameof(positions));

            Positions = positions;
        }
    }

    public class Order
    {
        public IList<IArea> Positions { get; }

        public Order(IList<IArea> positions)
        {
            if (positions == null || positions.Count == 0)
                throw new ArgumentNullException(nameof(positions));

            Positions = positions;
        }

        public Order(Cart cart)
        {
            var positions = cart.Positions.Select(x => x.Figure).ToArray();
            
            if (positions == null || positions.Length == 0)
                throw new ArgumentNullException(nameof(positions));

            Positions = positions;
        }

        public decimal GetTotal() =>
            Positions.Select(x => x switch
                {
                    ITriangle => (decimal)x.GetArea() * 1.2m,
                    ICircle => (decimal)x.GetArea() * 0.9m,
                    _ => 0
                })
                .Sum();
    }

    public interface IOrderStorage
    {
        // сохраняет оформленный заказ и возвращает сумму
        Task<decimal> SaveAsync(Order order);
    }
    
    
    internal class CreateOrderRequestHandler: IRequestHandler<Cart,decimal>
    {
        private readonly IFiguresStorage _figuresStorage;
        private readonly IOrderStorage _orderStorage;

        public CreateOrderRequestHandler(IFiguresStorage figuresStorage, IOrderStorage orderStorage)
        {
            _figuresStorage = figuresStorage;
            _orderStorage = orderStorage;
        }

        public async Task<decimal> Handle(Cart request, CancellationToken cancellationToken)
        {
            try
            {
                foreach (var position in request.Positions)
                {
                    if (position.Count < 1 || !_figuresStorage.CheckIfAvailable(position.Figure.ToString(), position.Count))
                        throw new ArgumentOutOfRangeException(nameof(position));

                    _figuresStorage.Reserve(position.Figure.ToString(), position.Count);
                }

                var order = new Order(request);
                var result = await _orderStorage.SaveAsync(order);

                return result;
            }
            catch
            {
                foreach (var position in request.Positions)
                {
                    _figuresStorage.RemoveReserve(position.Figure.ToString(), position.Count);
                }

                throw;
            }
        }
    }
    
    internal class FigureJsonConverter : JsonConverter<CartPosition>
    {
        public override CartPosition Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException();

            if (!reader.Read() || reader.TokenType != JsonTokenType.PropertyName || reader.GetString() != "type")
                throw new JsonException();

            if (!reader.Read() || reader.TokenType != JsonTokenType.String)
                throw new JsonException();

            var typeString = reader.GetString();
            if (string.IsNullOrEmpty(typeString))
                throw new JsonException();

            if (!reader.Read() || reader.TokenType != JsonTokenType.PropertyName || reader.GetString() != "figure")
                throw new JsonException();

            if (!reader.Read() || reader.TokenType != JsonTokenType.String)
                throw new JsonException();

            var data = reader.GetString();
            if (string.IsNullOrEmpty(data))
                throw new JsonException();

            IArea figure = null;
            switch (typeString)
            {
                case "Circle":
                    figure = JsonSerializer.Deserialize<Circle>(data);
                    break;
                
                case "Triangle":
                    figure = JsonSerializer.Deserialize<Triangle>(data);
                    break;
                
                case "Sqare":
                    figure = JsonSerializer.Deserialize<Square>(data);
                    break;
            }

            if (!reader.Read() || reader.TokenType != JsonTokenType.PropertyName || reader.GetString() != "count")
                throw new JsonException();

            if (!reader.Read() || reader.TokenType != JsonTokenType.Number)
                throw new JsonException();

            var count = reader.GetInt32();

            if (!reader.Read() || reader.TokenType != JsonTokenType.EndObject)
                throw new JsonException();

            return new CartPosition(figure, count);
        }

        public override void Write(Utf8JsonWriter writer, CartPosition value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("type");
            writer.WriteStringValue(value.Figure.GetType().Name);

            writer.WritePropertyName("figure");
            writer.WriteStringValue(JsonSerializer.Serialize(value.Figure, value.Figure.GetType()));

            writer.WriteNumber("count", value.Count);
            writer.WriteEndObject();
        }
    }
	
	[ApiController]
	[Route("[controller]")]
	public class FiguresController : ControllerBase
	{
		private readonly IMediator _mediator;

		public CartController(IMediator mediator)
		{
			_mediator = mediator;
		}
		

		[HttpPost]
		public async Task<ActionResult> Order([FromBody] Cart cart, CancellationToken token)
		{
			var result = await _mediator.Send(cart, token);
			return new OkObjectResult(result);
		}
	}
    
    
    /* Cart Json
        {
          "positions": [
            {
              "type": "Circle",
              "figure": "{\"Radius\":10}",
              "count": 10
            },
            {
              "type": "Triangle",
              "figure": "{\"SideA\":10,\"SideB\":10,\"SideC\":10}",
              "count": 2
            },
            {
              "type": "Sqare",
              "figure": "{\"SideA\":10.23,\"SideB\":10.23}",
              "count": 1
            }
          ]
        }
     */
}