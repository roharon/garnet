﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Garnet.server;
using NUnit.Framework;
using StackExchange.Redis;
using SetOperation = StackExchange.Redis.SetOperation;

namespace Garnet.test
{
    [TestFixture]
    public class RespSetTest
    {
        GarnetServer server;

        [SetUp]
        public void Setup()
        {
            TestUtils.DeleteDirectory(TestUtils.MethodTestDir, wait: true);
            server = TestUtils.CreateGarnetServer(TestUtils.MethodTestDir, lowMemory: true);
            server.Start();
        }

        [TearDown]
        public void TearDown()
        {
            server.Dispose();
            TestUtils.DeleteDirectory(TestUtils.MethodTestDir);
        }

        #region SEClientTests
        [Test]
        [TestCase("")]
        [TestCase("myset")]
        public void CandDoSaddBasic(string key)
        {
            using var redis = ConnectionMultiplexer.Connect(TestUtils.GetConfig());
            var db = redis.GetDatabase(0);

            var result = db.SetAdd(key, "Hello");
            Assert.IsTrue(result);

            result = db.SetAdd(key, "World");
            Assert.IsTrue(result);

            result = db.SetAdd(key, "World");
            Assert.IsFalse(result);

            var emptySetKey = $"{key}_empty";
            var added = db.SetAdd(key, []);
            Assert.AreEqual(0, added);

            result = db.KeyExists(emptySetKey);
            Assert.IsFalse(result);
        }

        [Test]
        public void CheckEmptySetKeyRemoved()
        {
            using var redis = ConnectionMultiplexer.Connect(TestUtils.GetConfig());
            var key = new RedisKey("user1:set");
            var db = redis.GetDatabase(0);
            var members = new[] { new RedisValue("Hello"), new RedisValue("World") };
            var result = db.SetAdd(key, members);
            Assert.AreEqual(2, result);

            var actualMembers = db.SetPop(key, 2);
            Assert.AreEqual(members.Length, actualMembers.Length);

            var keyExists = db.KeyExists(key);
            Assert.IsFalse(keyExists);
        }

        [Test]
        public void CanAddAndListMembers()
        {
            using var redis = ConnectionMultiplexer.Connect(TestUtils.GetConfig());
            var db = redis.GetDatabase(0);
            var result = db.SetAdd(new RedisKey("user1:set"), ["Hello", "World", "World"]);
            Assert.AreEqual(2, result);

            var members = db.SetMembers(new RedisKey("user1:set"));
            Assert.AreEqual(2, members.Length);

            var response = db.Execute("MEMORY", "USAGE", "user1:set");
            var actualValue = ResultType.Integer == response.Resp2Type ? Int32.Parse(response.ToString()) : -1;
            var expectedResponse = 272;
            Assert.AreEqual(expectedResponse, actualValue);
        }

        [Test]
        public void CanCheckIfMemberExistsInSet()
        {
            using var redis = ConnectionMultiplexer.Connect(TestUtils.GetConfig());
            var db = redis.GetDatabase(0);
            var key = new RedisKey("user1:set");

            db.KeyDelete(key);

            db.SetAdd(key, ["Hello", "World"]);

            var existingMemberExists = db.SetContains(key, "Hello");
            Assert.IsTrue(existingMemberExists);

            var nonExistingMemberExists = db.SetContains(key, "NonExistingMember");
            Assert.IsFalse(nonExistingMemberExists);

            var setDoesNotExist = db.SetContains("NonExistingSet", "AnyMember");
            Assert.IsFalse(setDoesNotExist);
        }

        [Test]
        public void CanAddAndGetAllMembersWithPendingStatus()
        {
            using var redis = ConnectionMultiplexer.Connect(TestUtils.GetConfig());
            var db = redis.GetDatabase(0);

            var nVals = 100;
            RedisValue[] values = new RedisValue[nVals];
            for (int i = 0; i < 100; i++)
            {
                values[i] = ($"val-{i + 1}");
            }

            for (int j = 0; j < 25; j++)
            {
                var nAdded = db.SetAdd($"Set_Test-{j + 1}", values);
                Assert.AreEqual(nVals, nAdded);
            }

            var members = db.SetMembers(new RedisKey("Set_Test-10"));
            Assert.AreEqual(100, members.Length);
        }

        [Test]
        public void CanReturnEmptySet()
        {
            using var redis = ConnectionMultiplexer.Connect(TestUtils.GetConfig());
            var db = redis.GetDatabase(0);
            _ = db.SetMembers(new RedisKey("myset"));

            var response = db.Execute("MEMORY", "USAGE", "myset");
            var actualValue = ResultType.Integer == response.Resp2Type ? int.Parse(response.ToString()) : -1;
            var expectedResponse = -1;
            Assert.AreEqual(expectedResponse, actualValue);
        }

        [Test]
        public void CanDoMembersWhenEmptyKey()
        {
            using var redis = ConnectionMultiplexer.Connect(TestUtils.GetConfig());
            var db = redis.GetDatabase(0);

            var empty = "";

            var addResult = db.SetAdd(empty, ["one", "two", "three", "four", "five"]);
            Assert.AreEqual(5, addResult);

            var result = db.SetMembers(empty);
            Assert.AreEqual(5, result.Length);
            var strResult = result.Select(r => r.ToString());
            var expectedResult = new[] { "one", "two", "three", "four", "five" };
            Assert.IsTrue(expectedResult.OrderBy(t => t).SequenceEqual(strResult.OrderBy(t => t)));
        }

        [Test]
        public void CanRemoveField()
        {
            using var redis = ConnectionMultiplexer.Connect(TestUtils.GetConfig());
            var db = redis.GetDatabase(0);
            var key = new RedisKey("user1:set");
            var result = db.SetAdd(key, ["ItemOne", "ItemTwo", "ItemThree", "ItemFour"]);
            Assert.AreEqual(4, result);

            var existingMemberExists = db.SetContains(key, "ItemOne");
            Assert.IsTrue(existingMemberExists, "Existing member 'ItemOne' does not exist in the set.");

            var memresponse = db.Execute("MEMORY", "USAGE", "user1:set");
            var actualValue = ResultType.Integer == memresponse.Resp2Type ? Int32.Parse(memresponse.ToString()) : -1;
            var expectedResponse = 424;
            Assert.AreEqual(expectedResponse, actualValue);

            var response = db.SetRemove(key, new RedisValue("ItemOne"));
            Assert.AreEqual(true, response);

            memresponse = db.Execute("MEMORY", "USAGE", "user1:set");
            actualValue = ResultType.Integer == memresponse.Resp2Type ? Int32.Parse(memresponse.ToString()) : -1;
            expectedResponse = 352;
            Assert.AreEqual(expectedResponse, actualValue);

            response = db.SetRemove(key, new RedisValue("ItemFive"));
            Assert.AreEqual(false, response);

            memresponse = db.Execute("MEMORY", "USAGE", "user1:set");
            actualValue = ResultType.Integer == memresponse.Resp2Type ? Int32.Parse(memresponse.ToString()) : -1;
            expectedResponse = 352;
            Assert.AreEqual(expectedResponse, actualValue);

            var longResponse = db.SetRemove(key, ["ItemTwo", "ItemThree"]);
            Assert.AreEqual(2, longResponse);

            memresponse = db.Execute("MEMORY", "USAGE", "user1:set");
            actualValue = ResultType.Integer == memresponse.Resp2Type ? Int32.Parse(memresponse.ToString()) : -1;
            expectedResponse = 200;
            Assert.AreEqual(expectedResponse, actualValue);

            var members = db.SetMembers(key);
            Assert.AreEqual(1, members.Length);

            response = db.SetRemove(key, new RedisValue("ItemFour"));
            Assert.IsTrue(response);

            var exists = db.KeyExists(key);
            Assert.IsFalse(exists);
        }

        [Test]
        public void CanUseSScanNoParameters()
        {
            using var redis = ConnectionMultiplexer.Connect(TestUtils.GetConfig());
            var db = redis.GetDatabase(0);

            // SSCAN without key
            try
            {
                db.Execute("SSCAN");
                Assert.Fail();
            }
            catch (RedisServerException e)
            {
                var expectedErrorMessage = string.Format(CmdStrings.GenericErrWrongNumArgs, nameof(Garnet.server.SetOperation.SSCAN));
                Assert.AreEqual(expectedErrorMessage, e.Message);
            }

            // Use setscan on non existing key
            var items = db.SetScan(new RedisKey("foo"), new RedisValue("*"), pageSize: 10);
            Assert.IsEmpty(items, "Failed to use SetScan on non existing key");

            RedisValue[] entries = ["item-a", "item-b", "item-c", "item-d", "item-e", "item-aaa"];

            // Add some items
            var added = db.SetAdd("myset", entries);
            Assert.AreEqual(entries.Length, added);

            var members = db.SetScan(new RedisKey("myset"), new RedisValue("*"));
            Assert.IsTrue(((IScanningCursor)members).Cursor == 0);
            Assert.IsTrue(members.Count() == entries.Length);

            int i = 0;
            foreach (var item in members)
            {
                Assert.IsTrue(entries[i++].Equals(item));
            }

            // No matching elements
            members = db.SetScan(new RedisKey("myset"), new RedisValue("x"));
            Assert.IsTrue(((IScanningCursor)members).Cursor == 0);
            Assert.IsEmpty(members);
        }

        [Test]
        public void CanUseSScanWithMatch()
        {
            using var redis = ConnectionMultiplexer.Connect(TestUtils.GetConfig());
            var db = redis.GetDatabase(0);

            // Add some items
            var added = db.SetAdd("myset", ["aa", "bb", "cc", "dd", "ee", "aaf"]);
            Assert.AreEqual(6, added);

            var members = db.SetScan(new RedisKey("myset"), new RedisValue("*aa"));
            Assert.IsTrue(((IScanningCursor)members).Cursor == 0);
            Assert.IsTrue(members.Count() == 1);
            Assert.IsTrue(members.ElementAt(0).Equals("aa"));
        }

        [Test]
        public void CanUseSScanWithCollection()
        {
            using var redis = ConnectionMultiplexer.Connect(TestUtils.GetConfig());
            var db = redis.GetDatabase(0);
            var key = "myset";
            // Add some items
            var r = new Random();

            // Fill a new Set with 1000 random items 
            int n = 1000;
            var entries = new RedisValue[n];

            for (int i = 0; i < n; i++)
            {
                var memberId = r.Next(0, 10000000);
                entries[i] = new RedisValue($"member:{memberId}");
            }

            var setLen = db.SetAdd(key, entries);
            var members = db.SetScan(key, new RedisValue("member:*"), (Int32)setLen);
            Assert.IsTrue(((IScanningCursor)members).Cursor == 0);
            Assert.IsTrue(members.Count() == setLen);
        }

        [Test]
        public void CanDoSScanWithCursor()
        {
            using var redis = ConnectionMultiplexer.Connect(TestUtils.GetConfig());
            var db = redis.GetDatabase(0);

            var key = "myset";

            // create a new array of Set
            var setEntries = new RedisValue[1000];
            for (int i = 0; i < setEntries.Length; i++)
            {
                setEntries[i] = new RedisValue("value:" + i);
            }

            // set with items
            db.SetAdd(key, setEntries);

            int pageSize = 40;
            var response = db.SetScan(key, "*", pageSize: pageSize, cursor: 0);
            var cursor = ((IScanningCursor)response);
            var j = 0;
            long pageNumber = 0;
            long pageOffset = 0;

            // Consume the enumeration
            foreach (var i in response)
            {
                // Represents the *active* page of results (not the pending/next page of results as returned by SCAN/HSCAN/ZSCAN/SSCAN)
                pageNumber = cursor.Cursor;

                // The offset into the current page.
                pageOffset = cursor.PageOffset;
                j++;
            }

            // Assert the end of the enumeration was reached
            Assert.AreEqual(setEntries.Length, j);

            // Assert the cursor is at the end of the enumeration
            Assert.AreEqual(pageNumber + pageOffset, setEntries.Length - 1);

            var l = response.LastOrDefault();
            Assert.AreEqual(l, $"value:{setEntries.Length - 1}");
        }

        [Test]
        public void CanDoSetUnion()
        {
            using var redis = ConnectionMultiplexer.Connect(TestUtils.GetConfig());
            var db = redis.GetDatabase(0);
            var redisValues1 = new RedisValue[] { "item-a", "item-b", "item-c", "item-d" };
            var result = db.SetAdd(new RedisKey("key1"), redisValues1);
            Assert.AreEqual(4, result);

            result = db.SetAdd(new RedisKey("key2"), ["item-c"]);
            Assert.AreEqual(1, result);

            result = db.SetAdd(new RedisKey("key3"), ["item-a", "item-c", "item-e"]);
            Assert.AreEqual(3, result);

            var members = db.SetCombine(SetOperation.Union, ["key1", "key2", "key3"]);
            RedisValue[] entries = ["item-a", "item-b", "item-c", "item-d", "item-e"];
            Assert.AreEqual(5, members.Length);
            // assert two arrays are equal ignoring order
            Assert.IsTrue(members.OrderBy(x => x).SequenceEqual(entries.OrderBy(x => x)));

            members = db.SetCombine(SetOperation.Union, ["key1", "key2", "key3", "_not_exists"]);
            Assert.AreEqual(5, members.Length);
            Assert.IsTrue(members.OrderBy(x => x).SequenceEqual(entries.OrderBy(x => x)));

            members = db.SetCombine(SetOperation.Union, ["_not_exists_1", "_not_exists_2", "_not_exists_3"]);
            Assert.IsEmpty(members);

            members = db.SetCombine(SetOperation.Union, ["_not_exists_1", "key1", "_not_exists_2", "_not_exists_3"]);
            Assert.AreEqual(4, members.Length);
            Assert.IsTrue(members.OrderBy(x => x).SequenceEqual(redisValues1.OrderBy(x => x)));

            members = db.SetCombine(SetOperation.Union, ["key1", "key2"]);
            Assert.AreEqual(4, members.Length);
            Assert.IsTrue(members.OrderBy(x => x).SequenceEqual(redisValues1.OrderBy(x => x)));

            try
            {
                db.SetCombine(SetOperation.Union, []);
                Assert.Fail();
            }
            catch (RedisServerException e)
            {
                Assert.AreEqual(string.Format(CmdStrings.GenericErrWrongNumArgs, "SUNION"), e.Message);
            }
        }

        [Test]
        [TestCase("key")]
        [TestCase("")]
        public void CanDoSetUnionStore(string key)
        {
            using var redis = ConnectionMultiplexer.Connect(TestUtils.GetConfig());
            var db = redis.GetDatabase(0);

            var key1 = "key1";
            var key1Value = new RedisValue[] { "a", "b", "c" };

            var key2 = "key2";
            var key2Value = new RedisValue[] { "c", "d", "e" };

            var key3 = "key3";
            var key3Value = new RedisValue[] { };

            var key4 = "key4";
            var key4Value = new RedisValue[] { };

            var addResult = db.SetAdd(key1, key1Value);
            Assert.AreEqual(3, addResult);
            addResult = db.SetAdd(key2, key2Value);
            Assert.AreEqual(3, addResult);
            addResult = db.SetAdd(key3, key3Value);
            Assert.AreEqual(0, addResult);
            addResult = db.SetAdd(key4, key4Value);
            Assert.AreEqual(0, addResult);

            var result = db.SetCombineAndStore(SetOperation.Union, key, key1, key2);
            Assert.AreEqual(5, result);

            var membersResult = db.SetMembers(key);
            Assert.AreEqual(5, membersResult.Length);
            var strResult = membersResult.Select(m => m.ToString()).ToArray();
            var expectedResult = new[] { "a", "b", "c", "d", "e" };
            Assert.IsTrue(expectedResult.OrderBy(t => t).SequenceEqual(strResult.OrderBy(t => t)));

            result = db.SetCombineAndStore(SetOperation.Union, key, key3, key4);
            Assert.AreEqual(0, result);

            var exists = db.KeyExists(key);
            Assert.IsFalse(exists);
        }


        [Test]
        public void CanDoSetInter()
        {
            using var redis = ConnectionMultiplexer.Connect(TestUtils.GetConfig());
            var db = redis.GetDatabase(0);
            var redisValues1 = new RedisValue[] { "item-a", "item-b", "item-c", "item-d" };
            var result = db.SetAdd(new RedisKey("key1"), redisValues1);
            Assert.AreEqual(4, result);

            result = db.SetAdd(new RedisKey("key2"), ["item-c"]);
            Assert.AreEqual(1, result);

            result = db.SetAdd(new RedisKey("key3"), ["item-a", "item-c", "item-e"]);
            Assert.AreEqual(3, result);

            var members = db.SetCombine(SetOperation.Intersect, ["key1", "key2", "key3"]);
            RedisValue[] entries = ["item-c"];
            Assert.AreEqual(1, members.Length);
            // assert two arrays are equal ignoring order
            Assert.IsTrue(members.OrderBy(x => x).SequenceEqual(entries.OrderBy(x => x)));

            members = db.SetCombine(SetOperation.Intersect, ["key1", "key2", "key3", "_not_exists"]);
            Assert.IsEmpty(members);

            members = db.SetCombine(SetOperation.Intersect, ["_not_exists_1", "_not_exists_2", "_not_exists_3"]);
            Assert.IsEmpty(members);


            try
            {
                db.SetCombine(SetOperation.Intersect, []);
                Assert.Fail();
            }
            catch (RedisServerException e)
            {
                Assert.AreEqual(string.Format(CmdStrings.GenericErrWrongNumArgs, "SINTER"), e.Message);
            }
        }

        [Test]
        public void CanDoSetInterStore()
        {
            string key = "key";
            using var redis = ConnectionMultiplexer.Connect(TestUtils.GetConfig());
            var db = redis.GetDatabase(0);

            var key1 = "key1";
            var key1Value = new RedisValue[] { "a", "b", "c" };

            var key2 = "key2";
            var key2Value = new RedisValue[] { "c", "d", "e" };

            var key3 = "key3";
            var key3Value = new RedisValue[] { "d", "e" };

            var addResult = db.SetAdd(key1, key1Value);
            Assert.AreEqual(key1Value.Length, addResult);
            addResult = db.SetAdd(key2, key2Value);
            Assert.AreEqual(key2Value.Length, addResult);
            addResult = db.SetAdd(key3, key3Value);
            Assert.AreEqual(key3Value.Length, addResult);

            var result = db.SetCombineAndStore(SetOperation.Intersect, key, key1, key2);
            Assert.AreEqual(1, result);

            var membersResult = db.SetMembers(key);
            Assert.AreEqual(1, membersResult.Length);
            var strResult = membersResult.Select(m => m.ToString()).ToArray();
            var expectedResult = new[] { "c" };
            Assert.IsTrue(expectedResult.SequenceEqual(strResult));

            result = db.SetCombineAndStore(SetOperation.Intersect, key, key1, key3);
            Assert.AreEqual(0, result);

            var exists = db.KeyExists(key);
            Assert.IsFalse(exists);
        }


        [Test]
        [TestCase("key1", "key2")]
        [TestCase("", "key2")]
        [TestCase("key1", "")]
        public void CanDoSdiff(string key1, string key2)
        {
            using var redis = ConnectionMultiplexer.Connect(TestUtils.GetConfig());
            var db = redis.GetDatabase(0);

            var key1Value = new RedisValue[] { "a", "b", "c", "d" };

            var key2Value = new RedisValue[] { "c" };

            var addResult = db.SetAdd(key1, key1Value);
            Assert.AreEqual(4, addResult);
            addResult = db.SetAdd(key2, key2Value);
            Assert.AreEqual(1, addResult);

            var result = db.SetCombine(SetOperation.Difference, key1, key2);
            Assert.AreEqual(3, result.Length);
            var strResult = result.Select(r => r.ToString()).ToArray();
            var expectedResult = new[] { "a", "b", "d" };
            Assert.IsTrue(expectedResult.OrderBy(t => t).SequenceEqual(strResult.OrderBy(t => t)));
            Assert.IsFalse(strResult.Contains("c"));

            var key3 = "key3";
            var key3Value = new RedisValue[] { "a", "c", "e" };

            addResult = db.SetAdd(key3, key3Value);
            Assert.AreEqual(3, addResult);

            result = db.SetCombine(SetOperation.Difference, [new RedisKey(key1), new RedisKey(key2), new RedisKey(key3)]);
            Assert.AreEqual(2, result.Length);
            strResult = result.Select(r => r.ToString()).ToArray();
            expectedResult = ["b", "d"];
            Assert.IsTrue(expectedResult.OrderBy(t => t).SequenceEqual(strResult.OrderBy(t => t)));

            Assert.IsFalse(strResult.Contains("c"));
            Assert.IsFalse(strResult.Contains("e"));
        }

        [Test]
        public void CanDoSdiffStoreOverwrittenKey()
        {
            using var redis = ConnectionMultiplexer.Connect(TestUtils.GetConfig());
            var db = redis.GetDatabase(0);

            var key = "key";

            var key1 = "key1";
            var key1Value = new RedisValue[] { "a", "b", "c", "d" };

            var key2 = "key2";
            var key2Value = new RedisValue[] { "c" };

            var addResult = db.SetAdd(key1, key1Value);
            Assert.AreEqual(4, addResult);
            addResult = db.SetAdd(key2, key2Value);
            Assert.AreEqual(1, addResult);

            var result = db.SetCombineAndStore(SetOperation.Difference, key, key1, key2);
            Assert.AreEqual(3, int.Parse(result.ToString()));

            var membersResult = db.SetMembers("key");
            Assert.AreEqual(3, membersResult.Length);
            var strResult = membersResult.Select(m => m.ToString()).ToArray();
            var expectedResult = new[] { "a", "b", "d" };
            Assert.IsTrue(expectedResult.OrderBy(t => t).SequenceEqual(strResult.OrderBy(t => t)));
            Assert.IsFalse(Array.Exists(membersResult, t => t.ToString().Equals("c")));

            var key3 = "key3";
            var key3Value = new RedisValue[] { "a", "b", "c" };
            var key4 = "key4";
            var key4Value = new RedisValue[] { "a", "b" };

            addResult = db.SetAdd(key3, key3Value);
            Assert.AreEqual(3, addResult);
            addResult = db.SetAdd(key4, key4Value);
            Assert.AreEqual(2, addResult);

            result = db.SetCombineAndStore(SetOperation.Difference, key, key3, key4);
            Assert.AreEqual(1, (int)result);

            membersResult = db.SetMembers("key");
            Assert.AreEqual(1, membersResult.Length);
            Assert.IsTrue(Array.Exists(membersResult, t => t.ToString().Equals("c")));

            var key5 = "key5";
            var key5Value = new RedisValue[] { "a", "b", "c" };

            addResult = db.SetAdd(key5, key5Value);
            Assert.AreEqual(3, addResult);

            result = db.SetCombineAndStore(SetOperation.Difference, key, key3, key5);
            Assert.AreEqual(0, (int)result);

            var exists = db.KeyExists(key);
            Assert.IsFalse(exists);
        }

        [Test]
        [TestCase("myset", "myotherset")]
        [TestCase("", "myotherset")]
        [TestCase("myset", "")]
        public void CanDoSmoveBasic(string source, string destination)
        {
            using var redis = ConnectionMultiplexer.Connect(TestUtils.GetConfig());
            var db = redis.GetDatabase(0);

            var addResult = db.SetAdd(source, ["one"]);
            Assert.AreEqual(1, addResult);
            addResult = db.SetAdd(source, ["two"]);
            Assert.AreEqual(1, addResult);

            addResult = db.SetAdd(destination, ["three"]);
            Assert.AreEqual(1, addResult);

            var result = db.SetMove(source, destination, "two");
            Assert.IsTrue(result);

            var membersResult = db.SetMembers(source);
            Assert.AreEqual(1, membersResult.Length);

            var strResult = membersResult.Select(r => r.ToString()).ToArray();
            var expectedResult = new[] { "one" };
            Assert.IsTrue(expectedResult.OrderBy(t => t).SequenceEqual(strResult.OrderBy(t => t)));

            membersResult = db.SetMembers(destination);
            strResult = membersResult.Select(r => r.ToString()).ToArray();
            expectedResult = ["three", "two"];
            Assert.IsTrue(expectedResult.OrderBy(t => t).SequenceEqual(strResult.OrderBy(t => t)));

            result = db.SetMove(source, destination, "one");
            Assert.IsTrue(result);

            var exists = db.KeyExists(source);
            Assert.IsFalse(exists);

            membersResult = db.SetMembers(destination);
            strResult = membersResult.Select(r => r.ToString()).ToArray();
            expectedResult = ["three", "two", "one"];
            Assert.IsTrue(expectedResult.OrderBy(t => t).SequenceEqual(strResult.OrderBy(t => t)));
        }

        [Test]
        public void CanDoSRANDMEMBERWithCountCommandSE()
        {
            using var redis = ConnectionMultiplexer.Connect(TestUtils.GetConfig());
            var db = redis.GetDatabase(0);
            var key = new RedisKey("myset");
            var values = new HashSet<RedisValue> { new("one"), new("two"), new("three"), new("four"), new("five") };

            // Check SRANDMEMBER with non-existing key
            var member = db.SetRandomMember(key);
            Assert.IsTrue(member.IsNull);

            // Check SRANDMEMBER with non-existing key and count
            var members = db.SetRandomMembers(key, 3);
            Assert.IsEmpty(members);

            // Check ZRANDMEMBER with wrong number of arguments
            var ex = Assert.Throws<RedisServerException>(() => db.Execute("SRANDMEMBER", key, 3, "bla"));
            var expectedMessage = string.Format(CmdStrings.GenericErrWrongNumArgs, nameof(RespCommand.SRANDMEMBER));
            Assert.IsNotNull(ex);
            Assert.AreEqual(expectedMessage, ex.Message);

            // Check SRANDMEMBER with non-numeric count
            ex = Assert.Throws<RedisServerException>(() => db.Execute("SRANDMEMBER", key, "bla"));
            expectedMessage = Encoding.ASCII.GetString(CmdStrings.RESP_ERR_GENERIC_VALUE_IS_NOT_INTEGER);
            Assert.IsNotNull(ex);
            Assert.AreEqual(expectedMessage, ex.Message);

            // Add items to set
            var added = db.SetAdd(key, [.. values]);
            Assert.AreEqual(values.Count, added);

            // Check SRANDMEMBER without count
            member = db.SetRandomMember(key);
            Assert.IsTrue(values.Contains(member));

            // Check SRANDMEMBER with positive count (distinct)
            members = db.SetRandomMembers(key, 3);
            Assert.AreEqual(3, members.Length);
            Assert.AreEqual(3, members.Distinct().Count());
            Assert.IsTrue(members.All(values.Contains));

            // Check SRANDMEMBER with positive count (distinct) larger than set cardinality
            members = db.SetRandomMembers(key, 6);
            Assert.AreEqual(values.Count, members.Length);
            Assert.AreEqual(values.Count, members.Distinct().Count());
            Assert.IsTrue(members.All(values.Contains));

            // Check SRANDMEMBER with negative count (non-distinct)
            members = db.SetRandomMembers(key, -6);
            Assert.AreEqual(6, members.Length);
            Assert.GreaterOrEqual(values.Count, members.Distinct().Count());
            Assert.IsTrue(members.All(values.Contains));
        }

        #endregion


        #region LightClientTests


        [Test]
        public void CanAddAndListMembersLC()
        {
            using var lightClientRequest = TestUtils.CreateRequest();
            var response = lightClientRequest.SendCommand("SADD myset \"Hello\"");
            var expectedResponse = ":1\r\n";
            var strResponse = Encoding.ASCII.GetString(response).Substring(0, expectedResponse.Length);
            Assert.AreEqual(expectedResponse, strResponse);

            response = lightClientRequest.SendCommand("SADD myset \"World\"");
            strResponse = Encoding.ASCII.GetString(response).Substring(0, expectedResponse.Length);
            Assert.AreEqual(expectedResponse, strResponse);

            expectedResponse = ":0\r\n";
            response = lightClientRequest.SendCommand("SADD myset \"World\"");
            strResponse = Encoding.ASCII.GetString(response).Substring(0, expectedResponse.Length);
            Assert.AreEqual(expectedResponse, strResponse);

            response = lightClientRequest.SendCommand("SMEMBERS myset", 3);
            expectedResponse = "*2\r\n$7\r\n\"Hello\"\r\n$7\r\n\"World\"\r\n";
            strResponse = Encoding.ASCII.GetString(response).Substring(0, expectedResponse.Length);
            Assert.AreEqual(expectedResponse, strResponse);
        }

        [Test]
        public void CanCheckIfMemberExistsInSetLC()
        {
            using var lightClientRequest = TestUtils.CreateRequest();

            var response = lightClientRequest.SendCommand("SADD myset \"Hello\"");
            var expectedResponse = ":1\r\n";
            var strResponse = Encoding.ASCII.GetString(response).Substring(0, expectedResponse.Length);
            Assert.AreEqual(expectedResponse, strResponse);

            response = lightClientRequest.SendCommand("SADD myset \"World\"");
            strResponse = Encoding.ASCII.GetString(response).Substring(0, expectedResponse.Length);
            Assert.AreEqual(expectedResponse, strResponse);

            response = lightClientRequest.SendCommand("SISMEMBER myset \"Hello\"");
            expectedResponse = ":1\r\n";
            strResponse = Encoding.ASCII.GetString(response).Substring(0, expectedResponse.Length);
            Assert.AreEqual(expectedResponse, strResponse);

            response = lightClientRequest.SendCommand("SISMEMBER myset \"NonExistingMember\"");
            expectedResponse = ":0\r\n";
            strResponse = Encoding.ASCII.GetString(response).Substring(0, expectedResponse.Length);
            Assert.AreEqual(expectedResponse, strResponse);

            response = lightClientRequest.SendCommand("SISMEMBER NonExistingSet \"AnyMember\"");
            expectedResponse = ":0\r\n";
            strResponse = Encoding.ASCII.GetString(response).Substring(0, expectedResponse.Length);
            Assert.AreEqual(expectedResponse, strResponse);

            // Missing arguments
            response = lightClientRequest.SendCommand("SISMEMBER myset");
            expectedResponse = $"-{string.Format(CmdStrings.GenericErrWrongNumArgs, "SISMEMBER")}\r\n";
            strResponse = Encoding.ASCII.GetString(response).Substring(0, expectedResponse.Length);
            Assert.AreEqual(expectedResponse, strResponse);

            // Extra arguments
            response = lightClientRequest.SendCommand("SISMEMBER myset \"Hello\" \"ExtraArg\"");
            strResponse = Encoding.ASCII.GetString(response).Substring(0, expectedResponse.Length);
            Assert.AreEqual(expectedResponse, strResponse);
        }

        [Test]
        public void CanDoSCARDCommandLC()
        {
            CreateSet();
            using var lightClientRequest = TestUtils.CreateRequest();
            var response = lightClientRequest.SendCommand("SCARD myset");
            var expectedResponse = ":2\r\n";
            var strResponse = Encoding.ASCII.GetString(response).Substring(0, expectedResponse.Length);
            Assert.AreEqual(expectedResponse, strResponse);
        }


        [Test]
        public void CanReturnEmptySetLC()
        {
            using var lightClientRequest = TestUtils.CreateRequest();
            var response = lightClientRequest.SendCommand("SMEMBERS otherset", 1);

            // Empty array
            var expectedResponse = "*0\r\n";
            var strResponse = Encoding.ASCII.GetString(response).Substring(0, expectedResponse.Length);
            Assert.AreEqual(expectedResponse, strResponse);
        }

        [Test]
        public void CanDoSREMLC()
        {
            using var lightClientRequest = TestUtils.CreateRequest();
            var response = lightClientRequest.SendCommand("SADD myset ItemOne ItemTwo ItemThree ItemFour");
            var expectedResponse = ":4\r\n";
            var strResponse = Encoding.ASCII.GetString(response).Substring(0, expectedResponse.Length);
            Assert.AreEqual(expectedResponse, strResponse);

            response = lightClientRequest.SendCommand("SREM myset World");
            expectedResponse = ":0\r\n";
            strResponse = Encoding.ASCII.GetString(response).Substring(0, expectedResponse.Length);
            Assert.AreEqual(expectedResponse, strResponse);

            response = lightClientRequest.SendCommand("SREM myset ItemOne");
            expectedResponse = ":1\r\n";
            strResponse = Encoding.ASCII.GetString(response).Substring(0, expectedResponse.Length);
            Assert.AreEqual(expectedResponse, strResponse);

            response = lightClientRequest.SendCommand("SREM myset ItemTwo ItemThree");
            expectedResponse = ":2\r\n";
            strResponse = Encoding.ASCII.GetString(response).Substring(0, expectedResponse.Length);
            Assert.AreEqual(expectedResponse, strResponse);
        }

        [Test]
        public void CanDoSCARDCommandsLC()
        {
            using var lightClientRequest = TestUtils.CreateRequest();
            var response = lightClientRequest.SendCommands("SCARD fooset", "PING", 1, 1);
            var expectedResponse = ":0\r\n+PONG\r\n";
            var strResponse = Encoding.ASCII.GetString(response).Substring(0, expectedResponse.Length);
            Assert.AreEqual(expectedResponse, strResponse);
        }

        [Test]
        public void CanDoSRANDMEMBERWithCountCommandLC()
        {
            var myset = new HashSet<string> { "one", "two", "three", "four", "five" };

            // Check SRANDMEMBER with non-existing key
            using var lightClientRequest = TestUtils.CreateRequest();
            var response = lightClientRequest.SendCommand("SRANDMEMBER myset");
            var expectedResponse = "$-1\r\n";
            var strResponse = Encoding.ASCII.GetString(response).Substring(0, expectedResponse.Length);
            Assert.AreEqual(expectedResponse, strResponse);

            // Check SRANDMEMBER with non-existing key and count
            response = lightClientRequest.SendCommand("SRANDMEMBER myset 3");
            expectedResponse = "*0\r\n";
            strResponse = Encoding.ASCII.GetString(response).Substring(0, expectedResponse.Length);
            Assert.AreEqual(expectedResponse, strResponse);

            CreateLongSet();

            response = lightClientRequest.SendCommand("SRANDMEMBER myset", 1);
            var strLen = Encoding.ASCII.GetString(response).Substring(1, 1);
            var item = Encoding.ASCII.GetString(response).Substring(4, Int32.Parse(strLen));
            Assert.IsTrue(myset.Contains(item));

            // Get three random members
            response = lightClientRequest.SendCommand("SRANDMEMBER myset 3", 3);
            strResponse = Encoding.ASCII.GetString(response);
            Assert.AreEqual('*', strResponse[0]);

            var arrLenEndIdx = strResponse.IndexOf("\r\n", StringComparison.InvariantCultureIgnoreCase);
            Assert.IsTrue(arrLenEndIdx > 1);

            var strArrLen = Encoding.ASCII.GetString(response).Substring(1, arrLenEndIdx - 1);
            Assert.IsTrue(int.TryParse(strArrLen, out var arrLen));
            Assert.AreEqual(3, arrLen);

            // Get 6 random members and verify that at least two elements are the same
            response = lightClientRequest.SendCommand("SRANDMEMBER myset -6", 6);
            arrLenEndIdx = Encoding.ASCII.GetString(response).IndexOf("\r\n", StringComparison.InvariantCultureIgnoreCase);
            strArrLen = Encoding.ASCII.GetString(response).Substring(1, arrLenEndIdx - 1);
            Assert.IsTrue(int.TryParse(strArrLen, out arrLen));

            var members = new HashSet<string>();
            var repeatedMembers = false;
            for (int i = 0; i < arrLen; i++)
            {
                var member = Encoding.ASCII.GetString(response).Substring(arrLenEndIdx + 2, response.Length - arrLenEndIdx - 5);
                if (members.Contains(member))
                {
                    repeatedMembers = true;
                    break;
                }
                members.Add(member);
            }

            Assert.IsTrue(repeatedMembers, "At least two members are repeated.");
        }

        [Test]
        public void CanDoSPOPCommandLC()
        {
            var myset = new HashSet<string>
            {
                "one",
                "two",
                "three",
                "four",
                "five"
            };

            CreateLongSet();

            using var lightClientRequest = TestUtils.CreateRequest();
            var response = lightClientRequest.SendCommand("SPOP myset");
            var strLen = Encoding.ASCII.GetString(response).Substring(1, 1);
            var item = Encoding.ASCII.GetString(response).Substring(4, Int32.Parse(strLen));
            Assert.IsTrue(myset.Contains(item));

            response = lightClientRequest.SendCommand("SCARD myset");
            var expectedResponse = ":4\r\n";
            var strResponse = Encoding.ASCII.GetString(response).Substring(0, expectedResponse.Length);
            Assert.AreEqual(expectedResponse, strResponse);
        }

        [Test]
        public void CanDoSPOPWithCountCommandLC()
        {
            CreateLongSet();

            var lightClientRequest = TestUtils.CreateRequest();
            var response = lightClientRequest.SendCommand("SPOP myset 3", 3);
            var strResponse = Encoding.ASCII.GetString(response);
            Assert.AreEqual('*', strResponse[0]);

            var arrLenEndIdx = strResponse.IndexOf("\r\n", StringComparison.InvariantCultureIgnoreCase);
            Assert.IsTrue(arrLenEndIdx > 1);

            var strArrLen = Encoding.ASCII.GetString(response).Substring(1, arrLenEndIdx - 1);
            Assert.IsTrue(int.TryParse(strArrLen, out var arrLen));
            Assert.AreEqual(3, arrLen);

            var secondResponse = lightClientRequest.SendCommands("SCARD myset", "PING", 1, 1);
            var expectedResponse = ":2\r\n+PONG\r\n";
            strResponse = Encoding.ASCII.GetString(secondResponse).Substring(0, expectedResponse.Length);
            Assert.AreEqual(expectedResponse, strResponse);

            // Test for popping set until empty
            response = lightClientRequest.SendCommand("SPOP myset 2", 2);
            strResponse = Encoding.ASCII.GetString(response);
            Assert.AreEqual('*', strResponse[0]);

            arrLenEndIdx = strResponse.IndexOf("\r\n", StringComparison.InvariantCultureIgnoreCase);
            Assert.IsTrue(arrLenEndIdx > 1);

            strArrLen = Encoding.ASCII.GetString(response).Substring(1, arrLenEndIdx - 1);
            Assert.IsTrue(int.TryParse(strArrLen, out arrLen));
            Assert.AreEqual(2, arrLen);
        }

        [Test]
        public void CanDoSPOPWithMoreCountThanSetSizeCommandLC()
        {
            CreateLongSet();

            var lightClientRequest = TestUtils.CreateRequest();

            var response = lightClientRequest.SendCommand("SPOP myset 10", 5);

            var strResponse = Encoding.ASCII.GetString(response);
            Assert.AreEqual('*', strResponse[0]);

            var arrLenEndIdx = strResponse.IndexOf("\r\n", StringComparison.InvariantCultureIgnoreCase);
            Assert.IsTrue(arrLenEndIdx > 1);

            var strArrLen = Encoding.ASCII.GetString(response).Substring(1, arrLenEndIdx - 1);
            Assert.IsTrue(int.TryParse(strArrLen, out var arrLen));
            Assert.IsTrue(arrLen == 5);

            var lightClientRequest2 = TestUtils.CreateRequest();
            var response2 = lightClientRequest2.SendCommand("SADD myset one");
            var expectedResponse = ":1\r\n";
            strResponse = Encoding.ASCII.GetString(response2).Substring(0, expectedResponse.Length);
            Assert.AreEqual(expectedResponse, strResponse);

            response2 = lightClientRequest2.SendCommand("SCARD myset");
            expectedResponse = ":1\r\n";
            strResponse = Encoding.ASCII.GetString(response2).Substring(0, expectedResponse.Length);
            Assert.AreEqual(expectedResponse, strResponse);
        }

        [Test]
        public void CanDoSMOVECommandLC()
        {
            using var lightClientRequest = TestUtils.CreateRequest();

            // source set
            lightClientRequest.SendCommand("SADD \"mySourceSet\" \"oneS\"");
            lightClientRequest.SendCommand("SADD \"mySourceSet\" \"twoS\"");
            lightClientRequest.SendCommand("SADD \"mySourceSet\" \"threeS\"");
            lightClientRequest.SendCommand("SADD \"mySourceSet\" \"fourS\"");
            lightClientRequest.SendCommand("SADD \"mySourceSet\" \"common\"");

            // destination set
            lightClientRequest.SendCommand("SADD \"myDestinationSet\" \"oneD\"");
            lightClientRequest.SendCommand("SADD \"myDestinationSet\" \"twoD\"");
            lightClientRequest.SendCommand("SADD \"myDestinationSet\" \"threeD\"");
            lightClientRequest.SendCommand("SADD \"myDestinationSet\" \"fourD\"");
            lightClientRequest.SendCommand("SADD \"myDestinationSet\" \"common\"");

            var expectedSuccessfulResponse = ":1\r\n";
            var expectedFailureResponse = ":0\r\n";

            // Successful move
            var response = lightClientRequest.SendCommand("SMOVE \"mySourceSet\" \"myDestinationSet\" \"oneS\"");
            var strResponse = Encoding.ASCII.GetString(response).Substring(0, expectedSuccessfulResponse.Length);
            Assert.AreEqual(expectedSuccessfulResponse, strResponse);

            response = lightClientRequest.SendCommand("SISMEMBER \"mySourceSet\" \"oneS\"");
            var mySourceSetContainsMember = Encoding.ASCII.GetString(response).Substring(0, expectedFailureResponse.Length);

            response = lightClientRequest.SendCommand("SISMEMBER \"myDestinationSet\" \"oneS\"");
            var myDestinationSetContainsMember = Encoding.ASCII.GetString(response).Substring(0, expectedSuccessfulResponse.Length);

            Assert.AreEqual(expectedFailureResponse, mySourceSetContainsMember);
            Assert.AreEqual(expectedSuccessfulResponse, myDestinationSetContainsMember);

            // Source set doesn't exist
            response = lightClientRequest.SendCommand("SMOVE \"someRandomSet\" \"mySourceSet\" \"twoS\"");
            strResponse = Encoding.ASCII.GetString(response).Substring(0, expectedFailureResponse.Length);
            Assert.AreEqual(expectedFailureResponse, strResponse);

            // Destination set doesn't exist
            response = lightClientRequest.SendCommand("SMOVE \"mySourceSet\" \"someRandomSet\" \"twoS\"");
            strResponse = Encoding.ASCII.GetString(response).Substring(0, expectedSuccessfulResponse.Length);
            Assert.AreEqual(expectedSuccessfulResponse, strResponse);

            // Value not in source
            response = lightClientRequest.SendCommand("SMOVE \"mySourceSet\" \"mySourceSet\" \"notAValue\"");
            strResponse = Encoding.ASCII.GetString(response).Substring(0, expectedFailureResponse.Length);
            Assert.AreEqual(expectedFailureResponse, strResponse);

            // Move into self
            response = lightClientRequest.SendCommand("SMOVE \"mySourceSet\" \"mySourceSet\" \"twoS\"");
            strResponse = Encoding.ASCII.GetString(response).Substring(0, expectedFailureResponse.Length);
            Assert.AreEqual(expectedFailureResponse, strResponse);

            // Common value
            response = lightClientRequest.SendCommand("SMOVE \"mySourceSet\" \"myDestinationSet\" \"common\"");
            strResponse = Encoding.ASCII.GetString(response).Substring(0, expectedSuccessfulResponse.Length);
            Assert.AreEqual(expectedSuccessfulResponse, strResponse);

            response = lightClientRequest.SendCommand("SISMEMBER \"mySourceSet\" \"common\"");
            mySourceSetContainsMember = Encoding.ASCII.GetString(response).Substring(0, expectedFailureResponse.Length);

            response = lightClientRequest.SendCommand("SISMEMBER \"myDestinationSet\" \"common\"");
            myDestinationSetContainsMember = Encoding.ASCII.GetString(response).Substring(0, expectedSuccessfulResponse.Length);

            Assert.AreEqual(expectedFailureResponse, mySourceSetContainsMember);
            Assert.AreEqual(expectedSuccessfulResponse, myDestinationSetContainsMember);
        }

        [Test]
        public async Task CanDoSMOVECommandGC()
        {
            using var db = TestUtils.GetGarnetClient();
            db.Connect();

            //If set doesn't exist, then return 0.
            var response = await db.ExecuteForLongResultAsync("SMOVE", ["sourceSet", "destinationSet", "value"]);
            Assert.AreEqual(response, 0);
            await db.ExecuteForStringResultAsync("SADD", ["sourceSet", "sourceValue", "commonValue"]);
            await db.ExecuteForStringResultAsync("SADD", ["destinationSet", "destinationValue", "commonValue"]);

            //Same key.
            response = await db.ExecuteForLongResultAsync("SMOVE", ["sourceSet", "sourceSet", "sourceValue"]);
            Assert.AreEqual(response, 0);

            //Move non-common member.
            response = await db.ExecuteForLongResultAsync("SMOVE", ["sourceSet", "destinationSet", "sourceValue"]);
            Assert.AreEqual(response, 1);
            Assert.AreEqual(await db.ExecuteForLongResultAsync("SCARD", ["sourceSet"]), 1);
            Assert.AreEqual(await db.ExecuteForLongResultAsync("SCARD", ["destinationSet"]), 3);

            var sourceSetMembers = await db.ExecuteForStringArrayResultAsync("SMEMBERS", ["sourceSet"]);
            var destinationSetMembers = await db.ExecuteForStringArrayResultAsync("SMEMBERS", ["destinationSet"]);
            Assert.IsFalse(sourceSetMembers.Contains("sourceValue"));
            Assert.IsTrue(destinationSetMembers.Contains("sourceValue"));

            //Move common member.
            response = await db.ExecuteForLongResultAsync("SMOVE", ["sourceSet", "destinationSet", "commonValue"]);
            Assert.AreEqual(response, 1);
            Assert.AreEqual(await db.ExecuteForLongResultAsync("SCARD", ["sourceSet"]), 0);
            Assert.AreEqual(await db.ExecuteForLongResultAsync("SCARD", ["destinationSet"]), 3);

            sourceSetMembers = await db.ExecuteForStringArrayResultAsync("SMEMBERS", ["sourceSet"]);
            destinationSetMembers = await db.ExecuteForStringArrayResultAsync("SMEMBERS", ["destinationSet"]);
            Assert.IsFalse(sourceSetMembers.Contains("commonValue"));
            Assert.IsTrue(destinationSetMembers.Contains("commonValue"));
        }

        [Test]
        public void MultiWithNonExistingSet()
        {
            var lightClientRequest = TestUtils.CreateRequest();
            byte[] res;

            string expectedResponse = "+OK\r\n";

            res = lightClientRequest.SendCommand("MULTI");
            Assert.AreEqual(res.AsSpan().Slice(0, expectedResponse.Length).ToArray(), expectedResponse);

            //create set
            res = lightClientRequest.SendCommand("SADD MySet ItemOne");
            expectedResponse = "+QUEUED\r\n";
            Assert.AreEqual(res.AsSpan().Slice(0, expectedResponse.Length).ToArray(), expectedResponse);

            res = lightClientRequest.SendCommand("EXEC", 2);
            expectedResponse = "*1\r\n:1\r\n";
            Assert.AreEqual(res.AsSpan().Slice(0, expectedResponse.Length).ToArray(), expectedResponse);

            res = lightClientRequest.SendCommand("SMEMBERS MySet", 2);
            expectedResponse = "*1\r\n$7\r\nItemOne\r\n";
            Assert.AreEqual(res.AsSpan().Slice(0, expectedResponse.Length).ToArray(), expectedResponse);
        }

        [Test]
        public void CanDoSetUnionLC()
        {
            using var lightClientRequest = TestUtils.CreateRequest();
            var response = lightClientRequest.SendCommand("SADD myset ItemOne ItemTwo ItemThree ItemFour");
            var expectedResponse = ":4\r\n";
            var strResponse = Encoding.ASCII.GetString(response).Substring(0, expectedResponse.Length);
            Assert.AreEqual(expectedResponse, strResponse);

            response = lightClientRequest.SendCommand("SUNION myset another_set", 5);
            expectedResponse = "*4\r\n";
            strResponse = Encoding.ASCII.GetString(response).Substring(0, expectedResponse.Length);
            Assert.AreEqual(expectedResponse, strResponse);

            lightClientRequest.SendCommand("SADD another_set ItemOne ItemFive ItemTwo ItemSix ItemSeven");
            response = lightClientRequest.SendCommand("SUNION myset another_set", 8);
            expectedResponse = "*7\r\n";
            strResponse = Encoding.ASCII.GetString(response).Substring(0, expectedResponse.Length);
            Assert.AreEqual(expectedResponse, strResponse);

            response = lightClientRequest.SendCommand("SUNION myset no_exist_set", 5);
            expectedResponse = "*4\r\n";
            strResponse = Encoding.ASCII.GetString(response).Substring(0, expectedResponse.Length);
            Assert.AreEqual(expectedResponse, strResponse);

            response = lightClientRequest.SendCommand("SUNION no_exist_set myset no_exist_set another_set", 8);
            expectedResponse = "*7\r\n";
            strResponse = Encoding.ASCII.GetString(response).Substring(0, expectedResponse.Length);
            Assert.AreEqual(expectedResponse, strResponse);

            response = lightClientRequest.SendCommand("SUNION myset", 5);
            expectedResponse = "*4\r\n";
            strResponse = Encoding.ASCII.GetString(response).Substring(0, expectedResponse.Length);
            Assert.AreEqual(expectedResponse, strResponse);

            response = lightClientRequest.SendCommand("SUNION");
            expectedResponse = $"-{string.Format(CmdStrings.GenericErrWrongNumArgs, "SUNION")}\r\n";
            strResponse = Encoding.ASCII.GetString(response).Substring(0, expectedResponse.Length);
            Assert.AreEqual(expectedResponse, strResponse);
        }

        [Test]
        public void CanDoSunionStoreLC()
        {
            var lightClientRequest = TestUtils.CreateRequest();
            _ = lightClientRequest.SendCommand("SADD key1 a b c");
            _ = lightClientRequest.SendCommand("SADD key2 c d e");
            var response = lightClientRequest.SendCommand("SUNIONSTORE key key1 key2");
            var expectedResponse = ":5\r\n";
            Assert.AreEqual(expectedResponse, response.AsSpan().Slice(0, expectedResponse.Length).ToArray());

            var membersResponse = lightClientRequest.SendCommand("SMEMBERS key");
            expectedResponse = "*5\r\n$1\r\na\r\n$1\r\nb\r\n$1\r\nc\r\n$1\r\nd\r\n$1\r\ne\r\n";
            Assert.AreEqual(expectedResponse, membersResponse.AsSpan().Slice(0, expectedResponse.Length).ToArray());
        }

        [Test]
        public void CanDoSdiffLC()
        {
            var lightClientRequest = TestUtils.CreateRequest();
            lightClientRequest.SendCommand("SADD key1 a b c d");
            lightClientRequest.SendCommand("SADD key2 c");
            lightClientRequest.SendCommand("SADD key3 a c e");
            var response = lightClientRequest.SendCommand("SDIFF key1 key2 key3");
            var expectedResponse = "*2\r\n$1\r\nb\r\n$1\r\nd\r\n";
            Assert.AreEqual(expectedResponse, response.AsSpan().Slice(0, expectedResponse.Length).ToArray());
        }


        [Test]
        public void CanDoSinterLC()
        {
            var lightClientRequest = TestUtils.CreateRequest();
            lightClientRequest.SendCommand("SADD key1 a b c d");
            lightClientRequest.SendCommand("SADD key2 c");
            lightClientRequest.SendCommand("SADD key3 a c e");
            var response = lightClientRequest.SendCommand("SINTER key1 key2 key3");
            var expectedResponse = "*1\r\n$1\r\nc\r\n";
            Assert.AreEqual(expectedResponse, response.AsSpan().Slice(0, expectedResponse.Length).ToArray());
        }

        [Test]
        public void IntersectWithEmptySetReturnEmptySet()
        {
            var lightClientRequest = TestUtils.CreateRequest();
            lightClientRequest.SendCommand("SADD key1 a");

            var response = lightClientRequest.SendCommand("SINTER key1 key2");
            var expectedResponse = "*0\r\n";
            Assert.AreEqual(expectedResponse, response.AsSpan().Slice(0, expectedResponse.Length).ToArray());
        }

        [Test]
        public void IntersectWithNoKeysReturnError()
        {
            var lightClientRequest = TestUtils.CreateRequest();
            var response = lightClientRequest.SendCommand("SINTER");
            var expectedResponse = "-ERR wrong number of arguments for 'SINTER' command\r\n";
            Assert.AreEqual(expectedResponse, response.AsSpan().Slice(0, expectedResponse.Length).ToArray());
        }

        [Test]
        public void IntersectAndStoreWithNoKeysReturnError()
        {
            var lightClientRequest = TestUtils.CreateRequest();
            var response = lightClientRequest.SendCommand("SINTERSTORE");
            var expectedResponse = "-ERR wrong number of arguments for 'SINTERSTORE' command\r\n";
            Assert.AreEqual(expectedResponse, response.AsSpan().Slice(0, expectedResponse.Length).ToArray());
        }


        [Test]
        public void IntersectAndStoreWithNotExisingSetsOverwitesDestinationSet()
        {
            var lightClientRequest = TestUtils.CreateRequest();
            lightClientRequest.SendCommand("SADD key a");

            var SINTERSTOREResponse = lightClientRequest.SendCommand("SINTERSTORE key key1 key2 key3");
            var expectedSINTERSTOREResponse = ":0\r\n";
            Assert.AreEqual(expectedSINTERSTOREResponse, SINTERSTOREResponse.AsSpan().Slice(0, expectedSINTERSTOREResponse.Length).ToArray());

            var membersResponse = lightClientRequest.SendCommand("SMEMBERS key");
            var expectedResponse = "*0\r\n";
            Assert.AreEqual(expectedResponse, membersResponse.AsSpan().Slice(0, expectedResponse.Length).ToArray());
        }

        [Test]
        public void IntersectAndStoreWithNoSetsReturnErrWrongNumArgs()
        {
            var lightClientRequest = TestUtils.CreateRequest();
            var SINTERSTOREResponse = lightClientRequest.SendCommand("SINTERSTORE key");
            var expectedSINTERSTOREResponse = $"-{string.Format(CmdStrings.GenericErrWrongNumArgs, "SINTERSTORE")}\r\n";
            Assert.AreEqual(expectedSINTERSTOREResponse, SINTERSTOREResponse.AsSpan().Slice(0, expectedSINTERSTOREResponse.Length).ToArray());
        }


        [Test]
        public void CanDoSinterStoreLC()
        {
            var lightClientRequest = TestUtils.CreateRequest();
            lightClientRequest.SendCommand("SADD key1 a b c d");
            lightClientRequest.SendCommand("SADD key2 c");
            lightClientRequest.SendCommand("SADD key3 a c e");
            var response = lightClientRequest.SendCommand("SINTERSTORE key key1 key2 key3");
            var expectedResponse = ":1\r\n";
            Assert.AreEqual(expectedResponse, response.AsSpan().Slice(0, expectedResponse.Length).ToArray());

            var membersResponse = lightClientRequest.SendCommand("SMEMBERS key");
            expectedResponse = "*1\r\n$1\r\nc\r\n";
            Assert.AreEqual(expectedResponse, membersResponse.AsSpan().Slice(0, expectedResponse.Length).ToArray());
        }

        [Test]
        [TestCase("")]
        [TestCase("key")]
        public void CanDoSdiffStoreLC(string key)
        {
            var lightClientRequest = TestUtils.CreateRequest();
            _ = lightClientRequest.SendCommand("SADD key1 a b c d");
            _ = lightClientRequest.SendCommand("SADD key2 c");
            _ = lightClientRequest.SendCommand("SADD key3 a c e");
            var response = lightClientRequest.SendCommand($"SDIFFSTORE {key} key1 key2 key3");
            var expectedResponse = ":2\r\n";
            Assert.AreEqual(expectedResponse, response.AsSpan().Slice(0, expectedResponse.Length).ToArray());

            var membersResponse = lightClientRequest.SendCommand($"SMEMBERS {key}");
            expectedResponse = "*2\r\n$1\r\nb\r\n$1\r\nd\r\n";
            Assert.AreEqual(expectedResponse, membersResponse.AsSpan().Slice(0, expectedResponse.Length).ToArray());
        }

        #endregion


        #region NegativeTests

        [Test]
        public void CanDoSCARDCommandWhenKeyDoesNotExistLC()
        {
            using var lightClientRequest = TestUtils.CreateRequest();
            var response = lightClientRequest.SendCommand("SCARD fooset");
            var expectedResponse = ":0\r\n";
            var strResponse = Encoding.ASCII.GetString(response).Substring(0, expectedResponse.Length);
            Assert.AreEqual(expectedResponse, strResponse);
        }

        [Test]
        public void CanDoSPOPCommandWhenKeyDoesNotExistLC()
        {
            using var lightClientRequest = TestUtils.CreateRequest();
            var response = lightClientRequest.SendCommand("SPOP fooset");
            var expectedResponse = "$-1\r\n";
            var strResponse = Encoding.ASCII.GetString(response).Substring(0, expectedResponse.Length);
            Assert.AreEqual(expectedResponse, strResponse);
        }

        [Test]
        public void CanUseNotExistingSetwithSMembers()
        {
            using var lightClientRequest = TestUtils.CreateRequest();
            var response = lightClientRequest.SendCommand("SMEMBERS foo");
            var expectedResponse = "*0\r\n";
            var strResponse = Encoding.ASCII.GetString(response).Substring(0, expectedResponse.Length);
            Assert.AreEqual(expectedResponse, strResponse);
        }

        [Test]
        public void CanDoSdiffWhenKeyDoesNotExisting()
        {
            using var lightClientRequest = TestUtils.CreateRequest();
            var response = lightClientRequest.SendCommand("SDIFF foo");
            var expectedResponse = "*0\r\n";
            var strResponse = Encoding.ASCII.GetString(response).Substring(0, expectedResponse.Length);
            Assert.AreEqual(expectedResponse, strResponse);
        }

        [Test]
        public void CanDoSdiffStoreWhenMemberKeysNotExisting()
        {
            using var lightClientRequest = TestUtils.CreateRequest();
            var response = lightClientRequest.SendCommand("SDIFFSTORE key key1 key2 key3");
            var expectedResponse = ":0\r\n";
            var strResponse = Encoding.ASCII.GetString(response).Substring(0, expectedResponse.Length);
            Assert.AreEqual(expectedResponse, strResponse);

            var membersResponse = lightClientRequest.SendCommand("SMEMBERS key");
            expectedResponse = "*0\r\n";
            strResponse = Encoding.ASCII.GetString(membersResponse).Substring(0, expectedResponse.Length);
            Assert.AreEqual(expectedResponse, strResponse);
        }

        [Test]
        public void CanDoSunionStoreWhenMemberKeysNotExisting()
        {
            using var lightClientRequest = TestUtils.CreateRequest();
            var response = lightClientRequest.SendCommand("SUNIONSTORE key key1 key2 key3");
            var expectedResponse = ":0\r\n";
            var strResponse = Encoding.ASCII.GetString(response).Substring(0, expectedResponse.Length);
            Assert.AreEqual(expectedResponse, strResponse);

            var membersResponse = lightClientRequest.SendCommand("SMEMBERS key");
            expectedResponse = "*0\r\n";
            strResponse = Encoding.ASCII.GetString(membersResponse).Substring(0, expectedResponse.Length);
            Assert.AreEqual(expectedResponse, strResponse);
        }

        [Test]
        public void CanDoSinterStoreWhenMemberKeysNotExisting()
        {
            using var lightClientRequest = TestUtils.CreateRequest();
            var response = lightClientRequest.SendCommand("SINTERSTORE key key1 key2 key3");
            var expectedResponse = ":0\r\n";
            var strResponse = Encoding.ASCII.GetString(response).Substring(0, expectedResponse.Length);
            Assert.AreEqual(expectedResponse, strResponse);

            var membersResponse = lightClientRequest.SendCommand("SMEMBERS key");
            expectedResponse = "*0\r\n";
            strResponse = Encoding.ASCII.GetString(membersResponse).Substring(0, expectedResponse.Length);
            Assert.AreEqual(expectedResponse, strResponse);
        }

        [Test]
        public void CheckSetOperationsOnWrongTypeObjectSE()
        {
            using var redis = ConnectionMultiplexer.Connect(TestUtils.GetConfig());
            var db = redis.GetDatabase(0);

            var keys = new[] { new RedisKey("user1:obj1"), new RedisKey("user1:obj2") };
            var key1Values = new[] { new RedisValue("Hello"), new RedisValue("World") };
            var key2Values = new[] { new RedisValue("Hola"), new RedisValue("Mundo") };
            var values = new[] { key1Values, key2Values };

            // Set up different type objects
            RespTestsUtils.SetUpTestObjects(db, GarnetObjectType.List, keys, values);

            // SADD
            RespTestsUtils.CheckCommandOnWrongTypeObjectSE(() => db.SetAdd(keys[0], values[0]));
            // SREM
            RespTestsUtils.CheckCommandOnWrongTypeObjectSE(() => db.SetRemove(keys[0], values[0]));
            // SPOP
            RespTestsUtils.CheckCommandOnWrongTypeObjectSE(() => db.SetPop(keys[0], 2));
            // SMEMBERS
            RespTestsUtils.CheckCommandOnWrongTypeObjectSE(() => db.SetMembers(keys[0]));
            // SCARD
            RespTestsUtils.CheckCommandOnWrongTypeObjectSE(() => db.SetLength(keys[0]));
            // SSCAN
            RespTestsUtils.CheckCommandOnWrongTypeObjectSE(() => db.SetScan(keys[0], new RedisValue("*")).FirstOrDefault());
            // SMOVE
            RespTestsUtils.CheckCommandOnWrongTypeObjectSE(() => db.SetMove(keys[0], keys[1], values[0][0]));
            // SRANDMEMBER
            RespTestsUtils.CheckCommandOnWrongTypeObjectSE(() => db.SetRandomMember(keys[0]));
            // SISMEMBER
            RespTestsUtils.CheckCommandOnWrongTypeObjectSE(() => db.SetContains(keys[0], values[0][0]));
            // SUNION
            RespTestsUtils.CheckCommandOnWrongTypeObjectSE(() => db.SetCombine(SetOperation.Union, keys[0], keys[1]));
            // SUNIONSTORE
            RespTestsUtils.CheckCommandOnWrongTypeObjectSE(() => db.SetCombineAndStore(SetOperation.Union, keys[0], [keys[1]]));
            // SDIFF
            RespTestsUtils.CheckCommandOnWrongTypeObjectSE(() => db.SetCombine(SetOperation.Difference, keys[0], keys[1]));
            // SDIFFSTORE
            RespTestsUtils.CheckCommandOnWrongTypeObjectSE(() =>
                db.SetCombineAndStore(SetOperation.Difference, keys[0], [keys[1]]));
            // SINTER
            RespTestsUtils.CheckCommandOnWrongTypeObjectSE(() => db.SetCombine(SetOperation.Intersect, keys[0], keys[1]));
            // SINTERSTORE
            RespTestsUtils.CheckCommandOnWrongTypeObjectSE(() => db.SetCombineAndStore(SetOperation.Intersect, keys[0], [keys[1]]));
        }

        #endregion


        #region commonmethods

        private static void CreateSet()
        {
            using var lightClientRequest = TestUtils.CreateRequest();
            var response = lightClientRequest.SendCommand("SADD myset \"Hello\"", 1);
            response = lightClientRequest.SendCommand("SADD myset \"World\"", 1);
        }

        private static void CreateLongSet()
        {
            using var lightClientRequest = TestUtils.CreateRequest();
            var response = lightClientRequest.SendCommand("SADD myset one", 1);
            response = lightClientRequest.SendCommand("SADD myset two", 1);
            response = lightClientRequest.SendCommand("SADD myset three", 1);
            response = lightClientRequest.SendCommand("SADD myset four", 1);
            response = lightClientRequest.SendCommand("SADD myset five", 1);
        }
        #endregion
    }
}