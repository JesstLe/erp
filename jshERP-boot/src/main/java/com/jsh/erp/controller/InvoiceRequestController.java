package com.jsh.erp.controller;

import com.alibaba.fastjson.JSONObject;
import com.jsh.erp.base.BaseController;
import com.jsh.erp.datasource.entities.User;
import com.jsh.erp.service.UserService;
import com.jsh.erp.service.cashier.InvoiceRequestService;
import com.jsh.erp.utils.BaseResponseInfo;
import io.swagger.annotations.Api;
import io.swagger.annotations.ApiOperation;
import org.springframework.web.bind.annotation.*;

import javax.annotation.Resource;
import javax.servlet.http.HttpServletRequest;
import java.util.Map;

@RestController
@RequestMapping(value = "/cashier/invoiceRequest")
@Api(tags = {"发票申请(收银)"})
public class InvoiceRequestController extends BaseController {
    @Resource
    private InvoiceRequestService invoiceRequestService;

    @Resource
    private UserService userService;

    @GetMapping(value = "/list")
    @ApiOperation(value = "发票申请列表")
    public BaseResponseInfo list(@RequestParam(value = "depotId", required = false) Long depotId,
                                 @RequestParam(value = "status", required = false) String status,
                                 @RequestParam(value = "keyword", required = false) String keyword,
                                 @RequestParam(value = "currentPage", required = false) Integer currentPage,
                                 @RequestParam(value = "pageSize", required = false) Integer pageSize,
                                 HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        try {
            User userInfo = userService.getCurrentUser();
            Long tenantId = resolveTenantId(userInfo);
            Map<String, Object> data = invoiceRequestService.list(depotId, status, keyword, currentPage, pageSize, tenantId);
            res.code = 200;
            res.data = data;
        } catch (Exception e) {
            res.code = 500;
            res.data = e.getMessage() == null ? "获取失败" : e.getMessage();
        }
        return res;
    }

    @PutMapping(value = "/markIssued")
    @ApiOperation(value = "标记已开票(回填发票号与附件)")
    public BaseResponseInfo markIssued(@RequestBody JSONObject obj, HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        try {
            User userInfo = userService.getCurrentUser();
            Long tenantId = resolveTenantId(userInfo);
            Long processorUserId = userInfo == null ? null : userInfo.getId();
            int result = invoiceRequestService.markIssued(obj, tenantId, processorUserId, request);
            res.code = 200;
            res.data = result;
        } catch (Exception e) {
            res.code = 500;
            res.data = e.getMessage() == null ? "操作失败" : e.getMessage();
        }
        return res;
    }

    @PutMapping(value = "/reject")
    @ApiOperation(value = "驳回发票申请")
    public BaseResponseInfo reject(@RequestBody JSONObject obj, HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        try {
            User userInfo = userService.getCurrentUser();
            Long tenantId = resolveTenantId(userInfo);
            Long processorUserId = userInfo == null ? null : userInfo.getId();
            int result = invoiceRequestService.reject(obj, tenantId, processorUserId, request);
            res.code = 200;
            res.data = result;
        } catch (Exception e) {
            res.code = 500;
            res.data = e.getMessage() == null ? "操作失败" : e.getMessage();
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

