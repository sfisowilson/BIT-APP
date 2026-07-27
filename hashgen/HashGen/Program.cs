using BCrypt.Net;
var h1 = BCrypt.Net.BCrypt.HashPassword("admin123");
var h2 = BCrypt.Net.BCrypt.HashPassword("editor123");
var h3 = BCrypt.Net.BCrypt.HashPassword("advertiser123");
Console.WriteLine("admin123: " + h1 + " verify: " + BCrypt.Net.BCrypt.Verify("admin123", h1));
Console.WriteLine("editor123: " + h2 + " verify: " + BCrypt.Net.BCrypt.Verify("editor123", h2));
Console.WriteLine("advertiser123: " + h3 + " verify: " + BCrypt.Net.BCrypt.Verify("advertiser123", h3));
