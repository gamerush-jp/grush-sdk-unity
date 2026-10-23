var GRushBridgeLibrary = {
  $GRushBridge: {
    onRpc: 0,
    onNet: 0,
    unbind: null,
    unbindLocale: null,
    unbindPlayerStatePreview: null,

    api: function (name) {
      return typeof window === "undefined" ? null : window[name] || null;
    },

    localeApi: function () {
      return GRushBridge.api("GRushLocale") || GRushBridge.api("GameRushLocale");
    },

    launchApi: function () {
      return GRushBridge.api("GRushLaunch") || GRushBridge.api("GameRushLaunch");
    },

    // JsonUtility cannot read a dictionary, so the params travel as an array of key and value pairs.
    launchEntries: function (launch) {
      var params = launch && launch.params;
      var entries = [];
      if (params && typeof params === "object") {
        Object.keys(params).forEach(function (key) {
          if (typeof params[key] === "string") entries.push({ key: key, value: params[key] });
        });
      }
      return { entries: entries };
    },

    respond: function (requestId, ok, text) {
      if (!GRushBridge.onRpc) return;
      var size = lengthBytesUTF8(text) + 1;
      var ptr = _malloc(size);
      stringToUTF8(text, ptr, size);
      var callback = GRushBridge.onRpc;
      {{{ makeDynCall("viiii", "callback") }}}(requestId, ok, ptr, size - 1);
      _free(ptr);
    },

    fail: function (requestId, error) {
      var code = error && typeof error.code === "string" ? error.code : "internal";
      var message = error && error.message ? String(error.message) : "GameRush API call failed.";
      GRushBridge.respond(requestId, 0, JSON.stringify({ code: code, message: message }));
    },

    emit: function (kind, from, channel, seq, bytes, text) {
      if (!GRushBridge.onNet) return;
      var size = bytes ? bytes.length : lengthBytesUTF8(text) + 1;
      var ptr = _malloc(size > 0 ? size : 1);
      if (bytes) {
        HEAPU8.set(bytes, ptr);
      } else {
        stringToUTF8(text, ptr, size);
        size -= 1;
      }
      var callback = GRushBridge.onNet;
      {{{ makeDynCall("viiiiii", "callback") }}}(kind, from, channel, seq, ptr, size);
      _free(ptr);
    },

    bind: function (room) {
      if (GRushBridge.unbind) GRushBridge.unbind();
      var offs = [
        room.on("message", function (m) {
          GRushBridge.emit(1, m.from, m.channel === "unreliable" ? 1 : 0, m.seq, new Uint8Array(m.payload), null);
        }),
        room.on("peerjoin", function (peer) {
          GRushBridge.emit(2, peer.index, 0, 0, null, JSON.stringify(peer));
        }),
        room.on("peerleave", function (event) {
          GRushBridge.emit(3, event.index, 0, 0, null, "");
        }),
        room.on("host", function (event) {
          GRushBridge.emit(4, event.index, 0, 0, null, "");
        }),
        room.on("transportchange", function (event) {
          GRushBridge.emit(5, 0, 0, 0, null, String(event.transport || ""));
        }),
        room.on("error", function (event) {
          GRushBridge.emit(7, 0, 0, 0, null, JSON.stringify({
            code: String((event && event.code) || ""),
            message: String((event && event.message) || ""),
          }));
        }),
        room.on("close", function (event) {
          GRushBridge.emit(6, 0, 0, 0, null, String(event.reason || ""));
          if (GRushBridge.unbind) GRushBridge.unbind();
        }),
      ];
      GRushBridge.unbind = function () {
        GRushBridge.unbind = null;
        for (var i = 0; i < offs.length; i++) {
          if (typeof offs[i] === "function") offs[i]();
        }
      };
      return room.describe();
    },

    wrap: function (promise, key) {
      return promise.then(function (value) {
        var envelope = {};
        envelope[key] = value === undefined ? null : value;
        return envelope;
      });
    },

    // The runtime may resolve with the bare value or with the RPC result that already wraps it.
    envelope: function (promise, key) {
      return promise.then(function (value) {
        if (value && typeof value === "object" && !Array.isArray(value) && key in value) return value;
        var envelope = {};
        envelope[key] = value === undefined ? null : value;
        return envelope;
      });
    },

    // JsonUtility cannot read a null number, so each player state also says whether it has a rating.
    state: function (state) {
      if (!state || typeof state !== "object") return state;
      var copy = Object.assign({}, state);
      copy.hasRating = typeof state.rating === "number";
      copy.rating = copy.hasRating ? state.rating : 0;
      return copy;
    },

    states: function (list) {
      return Array.isArray(list) ? list.map(GRushBridge.state) : [];
    },

    // JsonUtility cannot read an object of any shape, so payloads and trade items also travel as JSON
    // strings the game hands to its own parser (or to JsonUtility with its own type).
    json: function (value) {
      return value && typeof value === "object" ? JSON.stringify(value) : "";
    },

    message: function (message) {
      var copy = Object.assign({}, message);
      copy.payloadJson = GRushBridge.json(message && message.payload);
      return copy;
    },

    inboxPage: function (page) {
      var copy = Object.assign({}, page);
      copy.messages = Array.isArray(page && page.messages) ? page.messages.map(GRushBridge.message) : [];
      return copy;
    },

    trade: function (trade) {
      if (!trade || typeof trade !== "object") return trade;
      var copy = Object.assign({}, trade);
      copy.offerJson = GRushBridge.json(trade.offer);
      copy.wantJson = GRushBridge.json(trade.want);
      copy.giveJson = GRushBridge.json(trade.give);
      return copy;
    },

    trades: function (list) {
      return Array.isArray(list) ? list.map(GRushBridge.trade) : [];
    },

    withTrade: function (value) {
      var copy = Object.assign({}, value);
      if (copy.trade) copy.trade = GRushBridge.trade(copy.trade);
      return copy;
    },

    tradePage: function (page) {
      var copy = Object.assign({}, page);
      copy.trades = GRushBridge.trades(page && page.trades);
      return copy;
    },

    myTrades: function (mine) {
      return {
        open: GRushBridge.trades(mine && mine.open),
        pending: GRushBridge.trades(mine && mine.pending),
      };
    },

    invoke: function (method, params) {
      var player = GRushBridge.api("GRushPlayer");
      var net = GRushBridge.api("GRushNet");
      var boards = GRushBridge.api("GRushLeaderboards");
      var states = GRushBridge.api("GRushPlayerState");
      var args = params || {};
      if (method === "player.getSelf" && player) return player.getSelf();
      if (method === "player.requestProfile" && player) return player.requestProfile();
      if (method === "player.revokeProfile" && player) return player.revokeProfile();
      if (method === "net.join" && net) return net.join(params).then(GRushBridge.bind);
      if (method === "net.leave" && net) {
        if (GRushBridge.unbind) GRushBridge.unbind();
        return net.room ? net.room.leave() : Promise.resolve(null);
      }
      if (method === "leaderboard.list" && boards) {
        return GRushBridge.wrap(boards.list(), "leaderboards");
      }
      if (method === "leaderboard.submit" && boards) {
        return GRushBridge.wrap(
          boards.submit(args.key, args.value, args.metadata, args.operationId),
          "result",
        );
      }
      if (method === "leaderboard.top" && boards) {
        return GRushBridge.wrap(boards.top(args.key, args), "leaderboard");
      }
      if (method === "leaderboard.aroundMe" && boards) {
        return GRushBridge.wrap(boards.aroundMe(args.key, args), "leaderboard");
      }
      if (method === "leaderboard.friends" && boards) {
        return GRushBridge.wrap(boards.friends(args.key, args), "leaderboard");
      }
      if (method === "playerState.getMine" && states) {
        return GRushBridge.wrap(states.getMine().then(GRushBridge.state), "state");
      }
      if (method === "playerState.setMine" && states) {
        // rating is in the params only when the runtime is new enough to read it (see GRushPlayerStateApi).
        var written =
          "rating" in args
            ? states.setMine(args.payload, args.baseRevision, { rating: args.rating })
            : states.setMine(args.payload, args.baseRevision);
        return GRushBridge.wrap(written.then(GRushBridge.state), "state");
      }
      if (method === "playerState.get" && states) {
        return GRushBridge.wrap(states.get(args.pseudoIds).then(GRushBridge.states), "states");
      }
      if (method === "playerState.report" && states) {
        return GRushBridge.wrap(states.report(args.pseudoId), "reported");
      }
      if (method === "playerState.sample" && states) {
        return GRushBridge.envelope(states.sample(args), "states").then(function (value) {
          return { states: GRushBridge.states(value.states) };
        });
      }
      var inbox = GRushBridge.api("GRushInbox");
      if (method === "inbox.send" && inbox) {
        return GRushBridge.envelope(inbox.send(args.to, args.kind, args.payload), "sent");
      }
      if (method === "inbox.list" && inbox) return inbox.list(args).then(GRushBridge.inboxPage);
      if (method === "inbox.markRead" && inbox) {
        return GRushBridge.envelope(inbox.markRead(args.ids), "updated");
      }
      if (method === "inbox.remove" && inbox) {
        return GRushBridge.envelope(inbox.remove(args.ids), "updated");
      }
      if (method === "inbox.report" && inbox) {
        return GRushBridge.envelope(inbox.report(args.messageId), "reported");
      }
      var trade = GRushBridge.api("GRushTrade");
      if (method === "trade.create" && trade) {
        return GRushBridge.envelope(trade.create(args), "trade").then(GRushBridge.withTrade);
      }
      if (method === "trade.list" && trade) return trade.list(args).then(GRushBridge.tradePage);
      if (method === "trade.mine" && trade) return trade.mine().then(GRushBridge.myTrades);
      if (method === "trade.accept" && trade) {
        return trade.accept(args.tradeId, args.give).then(GRushBridge.withTrade);
      }
      if (method === "trade.cancel" && trade) {
        return GRushBridge.envelope(trade.cancel(args.tradeId), "trade").then(GRushBridge.withTrade);
      }
      if (method === "trade.ack" && trade) return GRushBridge.envelope(trade.ack(args.ids), "acked");
      if (method === "trade.report" && trade) {
        return GRushBridge.envelope(trade.report(args.tradeId), "reported");
      }
      var locale = GRushBridge.localeApi();
      if (method === "locale.get" && locale) return locale.get();
      var launch = GRushBridge.launchApi();
      if (method === "launch.get" && launch) return launch.get().then(GRushBridge.launchEntries);
      var share = GRushBridge.api("GRushShare");
      if (method === "share.open" && share) return share.share(args);
      if (method === "share.getAvailability" && share) {
        return GRushBridge.wrap(share.isAvailable(), "available");
      }
      return Promise.reject({
        code: "unsupportedMethod",
        message: "GameRush does not expose " + method + " here.",
      });
    },

    run: function (requestId, method, params) {
      try {
        GRushBridge.invoke(method, params).then(
          function (value) {
            GRushBridge.respond(requestId, 1, JSON.stringify(value === undefined ? null : value));
          },
          function (error) {
            GRushBridge.fail(requestId, error);
          },
        );
      } catch (error) {
        GRushBridge.fail(requestId, error);
      }
    },

    parse: function (requestId, paramsPtr) {
      var raw = paramsPtr ? UTF8ToString(paramsPtr) : "";
      try {
        return { ok: true, params: raw ? JSON.parse(raw) : undefined };
      } catch (error) {
        GRushBridge.fail(requestId, { code: "invalidParams", message: "Params were not JSON." });
        return { ok: false };
      }
    },
  },

  GRushJsPresent: function () {
    return GRushBridge.api("GRushInfo") && GRushBridge.api("GRushNet") && GRushBridge.api("GRushPlayer") ? 1 : 0;
  },
  GRushJsPresent__deps: ["$GRushBridge"],

  GRushJsProtocolVersion: function () {
    var info = GRushBridge.api("GRushInfo");
    return info && typeof info.protocolVersion === "number" ? info.protocolVersion : 0;
  },
  GRushJsProtocolVersion__deps: ["$GRushBridge"],

  GRushJsInit: function (onRpc, onNet) {
    GRushBridge.onRpc = onRpc;
    GRushBridge.onNet = onNet;
  },
  GRushJsInit__deps: ["$GRushBridge", "malloc", "free"],

  GRushJsCall: function (requestId, methodPtr, paramsPtr) {
    var method = UTF8ToString(methodPtr);
    var parsed = GRushBridge.parse(requestId, paramsPtr);
    if (parsed.ok) GRushBridge.run(requestId, method, parsed.params);
  },
  GRushJsCall__deps: ["$GRushBridge", "malloc", "free"],

  GRushJsShare: function (requestId, paramsPtr, dataPtr, length) {
    var parsed = GRushBridge.parse(requestId, paramsPtr);
    if (!parsed.ok) return;
    var params = parsed.params || {};
    if (dataPtr && length > 0) params.image = new Blob([HEAPU8.slice(dataPtr, dataPtr + length)]);
    GRushBridge.run(requestId, "share.open", params);
  },
  GRushJsShare__deps: ["$GRushBridge", "malloc", "free"],

  GRushJsLocaleCurrent: function () {
    var locale = GRushBridge.localeApi();
    var snapshot = locale ? locale.current() : null;
    var text = snapshot ? JSON.stringify(snapshot) : "";
    var size = lengthBytesUTF8(text) + 1;
    var ptr = _malloc(size);
    stringToUTF8(text, ptr, size);
    return ptr;
  },
  GRushJsLocaleCurrent__deps: ["$GRushBridge", "malloc"],

  GRushJsLaunchCurrent: function () {
    var launch = GRushBridge.launchApi();
    var snapshot = launch ? launch.current() : null;
    var text = snapshot ? JSON.stringify(GRushBridge.launchEntries(snapshot)) : "";
    var size = lengthBytesUTF8(text) + 1;
    var ptr = _malloc(size);
    stringToUTF8(text, ptr, size);
    return ptr;
  },
  GRushJsLaunchCurrent__deps: ["$GRushBridge", "malloc"],

  GRushJsLocaleSubscribe: function (onLocale) {
    var locale = GRushBridge.localeApi();
    if (!locale || GRushBridge.unbindLocale) return;
    GRushBridge.unbindLocale = locale.onChange(function (snapshot) {
      var text = JSON.stringify(snapshot);
      var size = lengthBytesUTF8(text) + 1;
      var ptr = _malloc(size);
      stringToUTF8(text, ptr, size);
      {{{ makeDynCall("vii", "onLocale") }}}(ptr, size - 1);
      _free(ptr);
    });
  },
  GRushJsLocaleSubscribe__deps: ["$GRushBridge", "malloc", "free"],

  // The snapshot travels in the same {"state": ...} shape as playerState.getMine, so the game reads it the same way.
  GRushJsPlayerStatePreviewSubscribe: function (onPreview) {
    var states = GRushBridge.api("GRushPlayerState");
    if (!states || typeof states.onPreview !== "function" || GRushBridge.unbindPlayerStatePreview) return;
    try {
      GRushBridge.unbindPlayerStatePreview = states.onPreview(function (snapshot) {
        var text = JSON.stringify({ state: GRushBridge.state(snapshot) });
        var size = lengthBytesUTF8(text) + 1;
        var ptr = _malloc(size);
        stringToUTF8(text, ptr, size);
        {{{ makeDynCall("vii", "onPreview") }}}(ptr, size - 1);
        _free(ptr);
      });
    } catch (error) {
      GRushBridge.unbindPlayerStatePreview = null;
    }
  },
  GRushJsPlayerStatePreviewSubscribe__deps: ["$GRushBridge", "malloc", "free"],

  GRushJsPlayerStatePreviewUnsubscribe: function () {
    var unbind = GRushBridge.unbindPlayerStatePreview;
    GRushBridge.unbindPlayerStatePreview = null;
    if (typeof unbind === "function") {
      try {
        unbind();
      } catch (error) {}
    }
  },
  GRushJsPlayerStatePreviewUnsubscribe__deps: ["$GRushBridge"],

  GRushJsSend: function (dataPtr, length, channel, to) {
    var net = GRushBridge.api("GRushNet");
    if (!net || !net.room) return;
    try {
      net.room.send(HEAPU8.slice(dataPtr, dataPtr + length), {
        channel: channel === 1 ? "unreliable" : "reliable",
        to: to,
      });
    } catch (error) {}
  },
  GRushJsSend__deps: ["$GRushBridge"],
};

mergeInto(LibraryManager.library, GRushBridgeLibrary);
