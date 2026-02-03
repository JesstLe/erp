package com.jsh.erp.controller;

import com.alibaba.fastjson.JSONObject;
import com.jsh.erp.base.BaseController;
import com.jsh.erp.datasource.entities.CashierSession;
import com.jsh.erp.datasource.entities.User;
import com.jsh.erp.service.UserService;
import com.jsh.erp.service.cashier.CashierSessionService;
import com.jsh.erp.utils.BaseResponseInfo;
import io.swagger.annotations.Api;
import io.swagger.annotations.ApiOperation;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.web.bind.annotation.*;

import javax.annotation.Resource;
import javax.servlet.http.HttpServletRequest;
import java.util.Map;

@RestController
@RequestMapping(value = "/cashier/session")
@Api(tags = {"收银会话"})
public class CashierSessionController extends BaseController {
    private Logger logger = LoggerFactory.getLogger(CashierSessionController.class);

    @Resource
    private CashierSessionService cashierSessionService;

    @Resource
    private UserService userService;

    @PostMapping(value = "/open")
    @ApiOperation(value = "开台/创建会话")
    public BaseResponseInfo open(@RequestBody JSONObject obj, HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        try {
            User userInfo = userService.getCurrentUser();
            if (userInfo == null) {
                res.code = 500;
                res.data = "未获取到当前用户，请重新登录";
                return res;
            }
            Long tenantId = resolveTenantId(userInfo);
            CashierSession session = cashierSessionService.openSession(obj, tenantId, userInfo.getId(), request);
            res.code = 200;
            res.data = session;
        } catch (Exception e) {
            logger.error("开台失败，参数：{}", obj, e);
            res.code = 500;
            res.data = "开台失败：" + e.getMessage();
        }
        return res;
    }

    @PostMapping(value = "/close")
    @ApiOperation(value = "清台/关闭会话")
    public BaseResponseInfo close(@RequestBody JSONObject obj, HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        try {
            User userInfo = userService.getCurrentUser();
            Long tenantId = resolveTenantId(userInfo);
            int result = cashierSessionService.closeSession(obj, tenantId, request);
            res.code = 200;
            res.data = result;
        } catch (Exception e) {
            res.code = 500;
            res.data = "清台失败";
        }
        return res;
    }

    @GetMapping(value = "/currentBySeat")
    @ApiOperation(value = "获取座席当前会话")
    public BaseResponseInfo currentBySeat(@RequestParam("seatId") Long seatId, HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        try {
            User userInfo = userService.getCurrentUser();
            Long tenantId = resolveTenantId(userInfo);
            CashierSession session = cashierSessionService.getCurrentBySeatId(seatId, tenantId);
            res.code = 200;
            res.data = session;
        } catch (Exception e) {
            res.code = 500;
            res.data = "获取数据失败";
        }
        return res;
    }

    @PutMapping(value = "/bindMember")
    @ApiOperation(value = "绑定会员到会话")
    public BaseResponseInfo bindMember(@RequestBody JSONObject obj, HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        try {
            User userInfo = userService.getCurrentUser();
            Long tenantId = resolveTenantId(userInfo);
            int result = cashierSessionService.bindMember(obj, tenantId, request);
            res.code = 200;
            res.data = result;
        } catch (Exception e) {
            res.code = 500;
            res.data = "绑定失败";
        }
        return res;
    }

    @GetMapping(value = "/detail")
    @ApiOperation(value = "会话详情(服务+产品汇总)")
    public BaseResponseInfo detail(@RequestParam("sessionId") Long sessionId, HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        try {
            User userInfo = userService.getCurrentUser();
            Long tenantId = resolveTenantId(userInfo);
            Map<String, Object> data = cashierSessionService.getDetail(sessionId, tenantId);
            if (data == null) {
                res.code = 500;
                res.data = "会话不存在";
                return res;
            }
            res.code = 200;
            res.data = data;
        } catch (Exception e) {
            logger.error("获取会话详情失败，sessionId={}", sessionId, e);
            res.code = 500;
            res.data = "获取失败";
        }
        return res;
    }

    private Long resolveTenantId(User userInfo) {
        if (userInfo == null) {
            return null;
        }
        if ("admin".equals(userInfo.getLoginName())) {
            return null;
        }
        if (userInfo.getTenantId() == null || userInfo.getTenantId() == 0L) {
            return null;
        }
        return userInfo.getTenantId();
    }
}
