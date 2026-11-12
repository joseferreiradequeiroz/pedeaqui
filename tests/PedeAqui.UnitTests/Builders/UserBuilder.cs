using Bogus;
using PedeAqui.Domain.Entities;
using PedeAqui.Domain.Enums;

namespace PedeAqui.UnitTests.Builders;

public static class UserBuilder
{
    public static User Build(long id = 1, string role = Roles.CUSTOMER)
    {
        return new Faker<User>()
            .RuleFor(user => user.Id, _ => id)
            .RuleFor(user => user.Name, faker => faker.Person.FullName)
            .RuleFor(user => user.Email, faker => faker.Internet.Email())
            .RuleFor(user => user.Password, faker => faker.Internet.Password())
            .RuleFor(user => user.UserIdentifier, _ => Guid.NewGuid())
            .RuleFor(user => user.Role, _ => role)
            .Generate();
    }

    public static User Admin(long id = 99) => Build(id, Roles.ADMIN);
}
