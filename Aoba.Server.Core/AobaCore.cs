﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using LuminousVector.Aoba.Models;
using Nancy.Authentication.Stateless;
using LuminousVector.Aoba.Server.Credentials;
using MongoDB.Driver;
using MongoDB.Bson;
using MongoDB.Driver.GridFS;
using System.IO;
using LuminousVector.Aoba.DataStore;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Runtime.InteropServices.ComTypes;

namespace LuminousVector.Aoba.Server
{
	public class AobaCore
	{
#if !DEBUG
		public const string BASE_DIR = "/Storage/Aoba/";
		public const string HOST = "aoba.app";
#else
		public const string HOST = "localhost:4321";
		public const string BASE_DIR = "M:/Aoba/";
#endif

		public static MongoClient DBClient
		{
			get
			{
				if (AobaDB.mongoClient == null)
					return AobaDB.mongoClient = new MongoClient(DBCredentials.CONNECTION_STRING);
				else
					return AobaDB.mongoClient;
			}
		}

		public static IMongoDatabase DB
		{
			get
			{
				if (AobaDB.database == null)
					return AobaDB.database = DBClient.GetDatabase(DBCredentials.DB_Name);
				else
					return AobaDB.database;
			}
		}

		public static GridFSBucket GridFS
		{
			get
			{
				if (AobaDB.gfs == null)
					return AobaDB.gfs = new GridFSBucket(DB);
				else
					return AobaDB.gfs;
			}
		}

		public static IMongoCollection<BsonDocument> Users
		{
			get
			{
				if (AobaDB.users == null)
					return AobaDB.users = DB.GetCollection<BsonDocument>("Users");
				else
					return AobaDB.users;
			}
		}

		public static IMongoCollection<BsonDocument> Media
		{
			get
			{
				if (AobaDB.media == null)
					return AobaDB.media = DB.GetCollection<BsonDocument>("Media");
				else
					return AobaDB.media;
			}
		}



		private class AobaDB
		{
			internal static MongoClient mongoClient;
			internal static IMongoDatabase database;
			internal static GridFSBucket gfs;
			internal static IMongoCollection<BsonDocument> users;
			internal static IMongoCollection<BsonDocument> media;
		}

		

		private static byte[] JWTKey
		{
			get
			{
				if (_jwtKey == null)
					return _jwtKey = GenetateJWTKey();
				else
					return _jwtKey;
			}
		}

		private static byte[] _jwtKey;


		internal static StatelessAuthenticationConfiguration StatelessConfig { get; private set; } = new StatelessAuthenticationConfiguration(nancyContext =>
		{
			var jwt = nancyContext.Request.Headers.Authorization;
			if (string.IsNullOrWhiteSpace(jwt))
			{
				if (nancyContext.Request.Cookies.ContainsKey("token"))
					jwt = $"Bearer {nancyContext.Request.Cookies["token"]}";
				else
					return null;
			};
			try
			{

			return ValidateJWT(jwt.Remove(0, "Bearer ".Length));
			}catch
			{
				return null;
			}

			/*string ApiKey = nancyContext.Request.Cookies.FirstOrDefault(c => c.Key == "ApiKey").Value;
			return GetUserFromApiKey(ApiKey); */
		});

		private static byte[] GenetateJWTKey()
		{
			var dir = $"{BASE_DIR}JWT/";
			var file = $"{dir}secret.txt";
			if (File.Exists(file))
				return Encoding.ASCII.GetBytes(File.ReadAllText(file));
			var key = new byte[64];
			using(var rng = new RNGCryptoServiceProvider())
			{
				rng.GetBytes(key);
			}
			if (!Directory.Exists(dir))
				Directory.CreateDirectory(dir);
			File.WriteAllText(file, Encoding.ASCII.GetString(key));
			return key;
		}

		internal static string GetJWT(string apiKey, int durationDays = 7)
		{
			var handler = new JwtSecurityTokenHandler();
			var token = handler.CreateToken(new SecurityTokenDescriptor
			{
				Issuer = "Aoba.app",
				Audience = "Aoba",
				Expires = DateTime.UtcNow.AddDays(durationDays),
				Subject = new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.Authentication, apiKey)
				}),
				SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(JWTKey), SecurityAlgorithms.HmacSha256)
			});
			return handler.WriteToken(token);
		}

		internal static UserModel ValidateJWT(string token)
		{
			var handler = new JwtSecurityTokenHandler();
			SecurityToken validatedToken;
			var validation = new TokenValidationParameters
			{
				ClockSkew = TimeSpan.FromMinutes(1),
				ValidIssuer = "Aoba.app",
				ValidAudience = "Aoba",
				IssuerSigningKey = new SymmetricSecurityKey(JWTKey),
				ValidateIssuerSigningKey = true,
			};
			try
			{
				var claim = handler.ValidateToken(token, validation ,out validatedToken);
				return GetUserFromApiKey(claim.Claims.First().Value);
			}catch
			{
				return null;
			}
		}

		internal static string HashPassword(string password)
		{
			byte[] salt;
			new RNGCryptoServiceProvider().GetBytes(salt = new byte[16]);

			var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 10000);
			byte[] hash = pbkdf2.GetBytes(20);

			byte[] hashBytes = new byte[36];
			Array.Copy(salt, 0, hashBytes, 0, 16);
			Array.Copy(hash, 0, hashBytes, 16, 20);

			return Convert.ToBase64String(hashBytes);
		}

		internal static bool VerifyPassword(string password, string passwordHash)
		{
			if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(passwordHash))
				return false;
			/* Extract the bytes */
			byte[] hashBytes = Convert.FromBase64String(passwordHash);
			/* Get the salt */
			byte[] salt = new byte[16];
			Array.Copy(hashBytes, 0, salt, 0, 16);
			/* Compute the hash on the password the user entered */
			var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 10000);
			byte[] hash = pbkdf2.GetBytes(20);
			/* Compare the results */
			for (int i = 0; i < 20; i++)
				if (hashBytes[i + 16] != hash[i])
					return false;
			return true;
		}

		internal static UserModel GetUserFromApiKey(string apiKey)
		{
			if (string.IsNullOrWhiteSpace(apiKey))
				return null;
			apiKey = Sanitize(apiKey);
			//var user = Users.Find(u => u.GetValue("apiKeys").AsBsonArray.Contains(apiKey)).First();
			var user = Users.Find("{ 'apiKeys' : '" + apiKey + "' }").FirstOrDefault();
			if (user == null || user.IsBsonNull)
				return null;
			var um = new UserModel(user.GetValue("username").AsString, user.GetValue("id").AsString, user.GetValue("claims").AsBsonArray.Select(c => c.AsString));
			return um;
		}

		internal static UserModel GetUser(string id)
		{
			var user = Users.Find("{ id : '" + id + "'}").FirstOrDefault();
			if (user == null || user.IsBsonNull)
				return null;
			return new UserModel(user.GetValue("username").AsString, user.GetValue("id").AsString, user.GetValue("claims").AsBsonArray.Select(c => c.AsString));
		}

		internal static string ValidateUser(LoginCredentialsModel login)
		{
			login.Username = Sanitize(login.Username);
			//var userInfo = Users.Find(u => u.GetValue("username").Equals(login.Username)).First();
			var userInfo = Users.Find("{ username : '" + login.Username + "'}").FirstOrDefault();
			if (userInfo == null || userInfo.IsBsonNull)
				return null;
			if (VerifyPassword(login.Password, userInfo.GetValue("passHash").AsString))
				return GetApiKey(userInfo.GetValue("id").AsString);
			return null;
		}

		internal static string GetApiKey(string userId)
		{
			var user = Users.Find("{ id : '" + userId + "'}").FirstOrDefault();
			if (user == null || user.IsBsonNull)
				return null;
			var apiKey = user.GetValue("apiKeys").AsBsonArray.FirstOrDefault()?.AsString;
			if (string.IsNullOrWhiteSpace(apiKey))
				return RegisterNewApiKey(userId);
			else
				return apiKey;
		}

		internal static bool UserExists(string username)
		{
			username = Sanitize(username);
			return Users.Find("{ username : '" + username + "'}").CountDocuments() > 0;
		}

		internal static string RegisterNewApiKey(string userId)
		{
			var apiKey = Guid.NewGuid().ToString();
			Users.UpdateOne("{ id : '" + userId + "'}", "{ $push: { apiKeys : '" + apiKey + "'}}");
			return apiKey;
		}

		internal static bool RegisterUser(LoginCredentialsModel login, string token = null)
		{
			Console.WriteLine($"Creating new User: {login.Username} Token: {token}");
			bool requireToken = true;
			if (token == null)
				requireToken = false;
			try
			{
				UserModel referer = new UserModel(login.Username, ""); 
				if (requireToken)
					referer = ValidateRegistrationToken(token);
				if (referer != null)
				{
					var userID = GetNewID();
					var newUser = new BsonDocument
					{
						{ "username", Sanitize(login.Username) },
						{ "id", userID },
						{ "passHash", HashPassword(login.Password) },
						{ "claims", new BsonArray(new string[0]) },
						{ "apiKeys", new BsonArray(new string[0]) },
						{ "regTokens", new BsonArray(new string[0]) },
					};
					Users.InsertOne(newUser);
					if(requireToken)
						RemoveRegToken(token);
					return true;
				}
				return false;
			}catch(Exception e)
			{
				Console.WriteLine(e.ToString()); 
				return false;
			}
		}

		internal static void RemoveUser(string id) => Users.DeleteOne(u => u.GetValue("id").Equals("id"));

		internal static string GetNewRegToken(string userid)
		{
			var token = GetNewID();
			Users.UpdateOne("{ id : '" + userid + "'}", "{ $push: { regTokens: '" + token + "'} }");
			return token;
		}

		internal static void RemoveRegToken(string token)
		{
			token = Sanitize(token);
			Users.UpdateOne("{regTokens : '" + token + "'}", "{ $pull: { regTokens : '" + token + "' } }");
		}

		private const int MAX_BIN_DATA_SIZE = (int)1e6;

		internal static string AddMedia(string userId, MediaModel media)
		{
			media.id = GetNewID();
			//var useDB = true;//media.mediaStream.Length > MAX_BIN_DATA_SIZE;
			if (!Directory.Exists($"{BASE_DIR}{media.type.ToString()}"))
				Directory.CreateDirectory($"{BASE_DIR}{media.type.ToString()}");
			var uriName = $"{media.type.ToString()}/{media.id}{media.Ext}";
			//if (!useDB)
			//{
				using (var fs = new FileStream($"{BASE_DIR}{uriName}", FileMode.CreateNew))
				{
					media.mediaStream.Position = 0;
					media.mediaStream.CopyTo(fs);
					fs.FlushAsync();
				}
			//}
			//var mediaId = GridFS.UploadFromStream(media.id, media.mediaStream);
			Media.InsertOne(new BsonDocument
			{
				{ "id", media.id },
				{ "type", media.type },
				//{ "media", (useDB ? mediaId : BsonValue.Create(null) )},
				{ "mediaUri",  BsonValue.Create(uriName) },
				//{ "mediaBin", (useDB ? BsonValue.Create(null) : new BsonBinaryData(StreamToByteA(media.mediaStream))) },
				{ "fileName", media.fileName },
				{ "owner", userId },
				{ "views", 0 }
			});
			return media.id;
		}

		internal static void DeleteImage(string id)
		{
			var media = GetMedia(id);
			if (media == null)
				return;
			var uriName = $"{media.type}/{media.id}{media.Ext}";
			var fileName = $"{BASE_DIR}{uriName}";
			if (File.Exists(fileName))
				File.Delete(fileName);
			
		}

		internal static void RebuildDB(string userID)
		{
			DBClient.ListDatabaseNames().ForEachAsync(s => Console.WriteLine(s));
			var files = Directory.GetFiles(BASE_DIR, "", SearchOption.AllDirectories);
			Console.WriteLine(files.Length);
			for (int i = 0; i < files.Length; i++)
			{
				var media = new MediaModel
				{
					id = Path.GetFileNameWithoutExtension(files[i]),
					fileName = Path.GetFileName(files[i]),
					type = MediaModel.GetMediaType(files[i])
				};
				Console.WriteLine($"Writing to DB: {media.id} | {media.type} ");
				Media.InsertOne(new BsonDocument
				{
					{ "id", media.id },
					{ "type", media.type },
					//{ "media", (useDB ? mediaId : BsonValue.Create(null) )},
					{ "mediaUri",  $"{media.type.ToString()}/{media.fileName}" },
					//{ "mediaBin", (useDB ? BsonValue.Create(null) : new BsonBinaryData(StreamToByteA(media.mediaStream))) },
					{ "fileName", media.fileName },
					{ "owner", userID },
					{ "views", 0 }
				});
			}
			Console.ReadLine();
		}

		private static byte[] StreamToByteA(Stream stream)
		{
			byte[] bArray = new byte[stream.Length];
			stream.Read(bArray, 0, (int)stream.Length);
			return bArray;
		}
		//internal static string Sanitize(string data) => data.Replace(' ', )

		internal static MediaModel GetMedia(string id)
		{
			id = Sanitize(id.Replace(' ', '+'));
			var media = Media.Find("{id : '" + id + "'}").FirstOrDefault();
			if (media == null)
				return null;
			if (!media.TryGetValue("fileName", out BsonValue fn))
				fn = BsonValue.Create(null);
			Stream file = null;
			var type = (MediaModel.MediaType)media.GetValue("type").AsInt32;
			if (media.Contains("mediaUri"))
			{
				var mediaUri = media.GetValue("mediaUri");
				if (!mediaUri.IsBsonNull)
					file = File.OpenRead($"{BASE_DIR}{mediaUri}");
			}
			if (file == null)
			{
				var mediaID = media.GetValue("media");
				if (mediaID.IsBsonNull)
					file = new MemoryStream(((BsonBinaryData)media.GetValue("mediaBin")).AsByteArray);
				else
					file = GetMediaStream(mediaID);
			}
			return new MediaModel
			{
				id = id,
				type = type,
				//mediaStream = ,
				mediaStream = file,
				fileName = (fn.IsBsonNull ? $"{id}{media.GetValue("ext")}" : fn.AsString)
			};
		}
		internal static Stream GetMediaStream(BsonValue id)
		{
			var mediaFile = new MemoryStream();
			GridFS.DownloadToStream(id, mediaFile);
			mediaFile.Position = 0;
			return mediaFile;
		}

		internal static UserStatsModel GetUserStats(string userid) => new UserStatsModel
		{
			screenShotCount = (int)Media.Find("{ owner : '" + Sanitize(userid) + "'}").CountDocuments()
		};

		internal static void IncrementViewCount(string id)
		{
			id = Sanitize(id);
			Media.UpdateOne("{ id : '" + id + "'}", "{ $inc: { views : 1 } }");
		}

		internal static string GetNewID() => Convert.ToBase64String(Guid.NewGuid().ToByteArray()).Replace("=", "").Replace("/", "_").Replace(@"\", ".");

		public static string Sanitize(string raw)
		{
			raw = raw.Replace("<", "&lt");
			raw = raw.Replace(">", "&gt");
			raw = raw.Replace("\'", "&quot");
			raw = raw.Replace("\"", "&apos");
			return raw;
		}

		public static string UnSanitize(string sanitized)
		{
			sanitized = sanitized.Replace("&lt", "<");
			sanitized = sanitized.Replace("&gt", ">");
			sanitized = sanitized.Replace("&quot", "\'");
			sanitized = sanitized.Replace("&apos", "\"");
			sanitized = Uri.UnescapeDataString(sanitized);
			return sanitized;
		}

		internal static UserModel ValidateRegistrationToken(string token)
		{
			token = Sanitize(token);
			//var user = Users.Find(u => u.GetValue("regToken").AsBsonArray.Contains(token)).First();
			var user = Users.Find("{ regTokens : '" + token + "' }").FirstOrDefault();
			if (user == null)
				return null;
			var um = new UserModel(user.GetValue("username").AsString, user.GetValue("id").AsString, user.GetValue("claims").AsBsonArray.Select(c => c.AsString));
			return um;
		}
	}
}
